using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Threading.Tasks;
using NBXplorer.Configuration;
using System.Threading;
using NBitcoin;
using System.Collections.Concurrent;
using NBXplorer.DerivationStrategy;
using System.Text;
using NBXplorer.Models;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.ServiceBus;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using NBXplorer.Logging;

namespace NBXplorer.MessageBrokers
{
	public class AzureServiceBus : IHostedService
    {
		EventAggregator			_EventAggregator;
		ExplorerConfiguration	_explorerConfiguration;
		bool					_Disposed = false;
		CompositeDisposable		_subscriptions = new CompositeDisposable();
		IQueueClient			_queueBlk;
		IQueueClient			_queueTran;
		ExplorerConfiguration	_config;

		public AzureServiceBus(BitcoinDWaitersAccessor waiters, ChainProvider chainProvider,EventAggregator eventAggregator, IOptions<ExplorerConfiguration> config)
		{ 
			_EventAggregator = eventAggregator;			
			ChainProvider = chainProvider;
			Waiters = waiters.Instance;
			_config = config.Value;
			_queueBlk = new QueueClient(_config.AzureServiceBusConnectionString, _config.AzureServiceBusBlockQueue);
			_queueTran = new QueueClient(_config.AzureServiceBusConnectionString, _config.AzureServiceBusTransactionQueue);
		}
		
		public async Task StartAsync(CancellationToken cancellationToken)
		{
			if (_Disposed)
				throw new ObjectDisposedException(nameof(AzureServiceBus));


			string listenAllDerivationSchemes	= null;
			var	listenedBlocks					= new ConcurrentDictionary<string, string>();
			var listenedDerivations				= new ConcurrentDictionary<(Network, DerivationStrategyBase), DerivationStrategyBase>();

			_subscriptions.Add( _EventAggregator.Subscribe<Events.NewBlockEvent>( async o =>
			{
				if (listenedBlocks.ContainsKey(o.CryptoCode))
				{
					var chain = ChainProvider.GetChain(o.CryptoCode);
					if (chain == null)
						return;
					var block = chain.GetBlock(o.BlockId);
					if (block != null)
					{						
						var jblock = new JBlock()
						{
							CryptoCode = o.CryptoCode,
							Hash = block.Hash.ToString(),
							Height = block.Height,
							PreviousBlockHash = block?.Previous.ToString()
						};

						var jsonBlock = JsonConvert.SerializeObject(jblock);
						var message = new Message(Encoding.UTF8.GetBytes(jsonBlock));
						message.MessageId = block.Hash.ToString();							//Used for duplicate detection, if required.
						await _queueBlk.SendAsync(message);
					}
				}
			}));

			_subscriptions.Add(_EventAggregator.Subscribe<Events.NewTransactionMatchEvent>(async o =>
			{
				var network = Waiters.GetWaiter(o.CryptoCode);
				if (network == null)
					return;
				if (
				listenAllDerivationSchemes == "*" ||
				listenAllDerivationSchemes == o.CryptoCode ||
				listenedDerivations.ContainsKey((network.Network.NBitcoinNetwork, o.Match.DerivationStrategy)))
				{
					var chain = ChainProvider.GetChain(o.CryptoCode);
					if (chain == null)
						return;

					var blockHeader = o.BlockId == null ? null : chain.GetBlock(o.BlockId);
					var transactionData = ToTransactionResult(true, chain, new[] { o.SavedTransaction });

					var inputs = new List<JInput>();
					foreach (var ti in transactionData.Transaction.Inputs)
					{
						var ji = new JInput()
						{
							Address = ti.ScriptSig.Hash.ToString()
						};
						inputs.Add(ji);
					};


					var outputs = new List<JOutput>();

					foreach(var to in transactionData.Transaction.Outputs)
					{
						var jo = new JOutput()
						{
							Address = to.ScriptPubKey.GetDestinationAddress(network.Network.NBitcoinNetwork).ToString(),
							Value = to.Value.ToDecimal(MoneyUnit.Satoshi)
						};

						outputs.Add(jo);
					}

					var tx = new JSimpleTransaction()
					{
						CryptoCode = o.CryptoCode,
						BlockId = blockHeader?.Hash.ToString(),
						Confirmations = transactionData.Confirmations,
						Height = transactionData.Height,
						Timestamp = transactionData.Timestamp,
						TotalOut = transactionData.Transaction.TotalOut.ToDecimal(MoneyUnit.Satoshi),
						Version = transactionData.Transaction.Version,
						Inputs = inputs,
						Outputs = outputs,
						DerivationStrategy = o.Match.DerivationStrategy.ToString()
					};


					var jsonTran= JsonConvert.SerializeObject(tx);
					var message = new Microsoft.Azure.ServiceBus.Message(Encoding.UTF8.GetBytes(jsonTran));
					message.MessageId = o.SavedTransaction.GetHashCode().ToString();
					await _queueTran.SendAsync(message);
				}
			}));

			Logs.Configuration.LogInformation("Starting Azure Service Bus Message Broker");

			string listenedCryptos = "";

			//TODO: Add filters for message queue in future release. Currently listens to all configured crypto currencies for blocks and all Derivation Strategies for Transactions
			foreach ( var cc in _config.ChainConfigurations)
			{
				listenedBlocks.TryAdd(cc.CryptoCode, cc.CryptoCode);
				listenedCryptos += $"{cc.CryptoCode},";
			}
			Logs.Explorer.LogInformation($"Azure Service Bus Message Broker Blocks listening to {listenedCryptos}");


			listenAllDerivationSchemes = "*";
			Logs.Explorer.LogInformation($"Azure Service Bus Message Broker Transactions listening to all derivation schemes");

		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			Logs.Configuration.LogInformation("Stopping Azure Service Bus Message Broker");
			_Disposed = true;
			_subscriptions.Dispose();

			if (!_queueBlk.IsClosedOrClosing)
				await _queueBlk.CloseAsync();

			if (!_queueTran.IsClosedOrClosing)
				await _queueBlk.CloseAsync();
		}

		public ChainProvider ChainProvider
		{
			get; set;
		}
		public BitcoinDWaiters Waiters
		{
			get; set;
		}

		/// <summary>
		/// Duplicated from MainController - this should be moved to Utils
		/// </summary>
		/// <param name="includeTransaction"></param>
		/// <param name="chain"></param>
		/// <param name="result"></param>
		/// <returns></returns>
		private TransactionResult ToTransactionResult(bool includeTransaction, SlimChain chain, Repository.SavedTransaction[] result)
		{
			var noDate = NBitcoin.Utils.UnixTimeToDateTime(0);
			var oldest = result
							.Where(o => o.Timestamp != noDate)
							.OrderBy(o => o.Timestamp).FirstOrDefault() ?? result.First();

			var confBlock = result
						.Where(r => r.BlockHash != null)
						.Select(r => chain.GetBlock(r.BlockHash))
						.Where(r => r != null)
						.FirstOrDefault();

			var conf = confBlock == null ? 0 : chain.Height - confBlock.Height + 1;

			return new TransactionResult() { Confirmations = conf, BlockId = confBlock?.Hash, Transaction = includeTransaction ? oldest.Transaction : null, Height = confBlock?.Height, Timestamp = oldest.Timestamp };
		}
	}

	public class JBlock
	{
		public string CryptoCode { get; set; }
		public string Hash { get; set; }
		public int Height { get; set; }
		public string PreviousBlockHash { get; set; }
	}

	public class JOutput
	{
		public string Address { get; set; }
		public decimal Value { get; set; }
	}

	public class JInput
	{
		public string Address { get; set; }		
	}

	public class JSimpleTransaction
	{
		public string CryptoCode { get; set; }
		public string DerivationStrategy { get; set; }
		public string BlockId { get; set; }
		public int Confirmations { get; set; }
		public int? Height { get; set; }
		public DateTimeOffset Timestamp { get; set; }
		public decimal TotalOut { get; set; }
		public uint Version { get; set; }
		public List<JOutput> Outputs { get; set; }
		public List<JInput> Inputs { get; set; }
	}
}
