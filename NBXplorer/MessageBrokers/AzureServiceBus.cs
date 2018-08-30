using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBXplorer.Configuration;
using NBXplorer.Logging;
using Newtonsoft.Json;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer.MessageBrokers
{
	public class AzureServiceBus : IHostedService
	{
		EventAggregator _EventAggregator;
		bool _Disposed = false;
		CompositeDisposable _subscriptions = new CompositeDisposable();
		IQueueClient _queueBlk = null;
		IQueueClient _queueTran = null;
		ITopicClient _topicBlk = null;
		ITopicClient _topicTran = null;
		ExplorerConfiguration _config;
		JsonSerializerSettings _serializerSettings;

		public AzureServiceBus(BitcoinDWaitersAccessor waiters, ChainProvider chainProvider, EventAggregator eventAggregator, IOptions<ExplorerConfiguration> config, IOptions<MvcJsonOptions> jsonOptions)
		{
			_EventAggregator = eventAggregator;
			ChainProvider = chainProvider;
			Waiters = waiters.Instance;
			_config = config.Value;

			_serializerSettings = jsonOptions.Value.SerializerSettings;
		}

		public Task StartAsync(CancellationToken cancellationToken)
		{
			if (_Disposed)
				throw new ObjectDisposedException(nameof(AzureServiceBus));

			if (string.IsNullOrWhiteSpace(_config.AzureServiceBusConnectionString))
			{
				Logs.Explorer.LogInformation("[Azure Service Bus] No connection string configured - Azure service bus will not be used");
				return Task.CompletedTask;
			}

			if (!string.IsNullOrWhiteSpace(_config.AzureServiceBusBlockQueue))
				_queueBlk = new QueueClient(_config.AzureServiceBusConnectionString, _config.AzureServiceBusBlockQueue);

			if (!string.IsNullOrWhiteSpace(_config.AzureServiceBusTransactionQueue))
				_queueTran = new QueueClient(_config.AzureServiceBusConnectionString, _config.AzureServiceBusTransactionQueue);

			if (!string.IsNullOrWhiteSpace(_config.AzureServiceBusBlockTopic))
				_topicBlk = new TopicClient(_config.AzureServiceBusConnectionString, _config.AzureServiceBusBlockTopic);

			if (!string.IsNullOrWhiteSpace(_config.AzureServiceBusTransactionTopic))
				_topicTran = new TopicClient(_config.AzureServiceBusConnectionString, _config.AzureServiceBusTransactionTopic);


			_subscriptions.Add(_EventAggregator.Subscribe<Events.NewBlockEvent>(async o =>
			{
				var chain = ChainProvider.GetChain(o.CryptoCode);
				if (chain == null)
					return;
				var block = chain.GetBlock(o.BlockId);
				if (block != null)
				{
					var nbe = new Models.NewBlockEvent()
					{
						CryptoCode = o.CryptoCode,
						Hash = block.Hash,
						Height = block.Height,
						PreviousBlockHash = block?.Previous
					};

					string jsonMsg = nbe.ToJson(_serializerSettings);
					var bytes = Encoding.UTF8.GetBytes(jsonMsg);
					var message = new Message(bytes);
					message.ContentType = nbe.GetType().ToString();
					message.MessageId = block.Hash.ToString();          //Used for duplicate detection, if required.

					if (_topicBlk != null && !_topicBlk.IsClosedOrClosing)
						await _topicBlk.SendAsync(message);

					if (_queueBlk != null && !_queueBlk.IsClosedOrClosing)
						await _queueBlk.SendAsync(message);

				}
			}));

			_subscriptions.Add(_EventAggregator.Subscribe<Events.NewTransactionMatchEvent>(async o =>
			{
				var network = Waiters.GetWaiter(o.CryptoCode);
				if (network == null)
					return;

				var chain = ChainProvider.GetChain(o.CryptoCode);
				if (chain == null)
					return;

				var blockHeader = o.BlockId == null ? null : chain.GetBlock(o.BlockId);

				var txe = new Models.NewTransactionEvent()
				{
					CryptoCode = o.CryptoCode,
					DerivationStrategy = o.Match.DerivationStrategy,
					BlockId = blockHeader?.Hash,
					TransactionData = Utils.ToTransactionResult(true, chain, new[] { o.SavedTransaction }),
					Inputs = o.Match.Inputs,
					Outputs = o.Match.Outputs
				};

				string jsonMsg = txe.ToJson(_serializerSettings);
				var bytes = Encoding.UTF8.GetBytes(jsonMsg);

				var message = new Message(bytes);
				message.MessageId = o.SavedTransaction.GetHashCode().ToString();
				message.ContentType = txe.GetType().ToString();

				if (_topicTran != null && !_topicTran.IsClosedOrClosing)
					await _topicTran.SendAsync(message);

				if (_queueTran != null && !_queueTran.IsClosedOrClosing)
					await _queueTran.SendAsync(message);


			}));

			Logs.Configuration.LogInformation("Starting Azure Service Bus Message Broker");
			return Task.CompletedTask;
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			Logs.Configuration.LogInformation("Stopping Azure Service Bus Message Broker");
			_Disposed = true;
			_subscriptions.Dispose();

			if (_queueBlk!=null && !_queueBlk.IsClosedOrClosing)
				await _queueBlk.CloseAsync();

			if (_queueTran !=null &&  !_queueTran.IsClosedOrClosing)
				await _queueBlk.CloseAsync();

			if (_topicBlk != null && !_topicBlk.IsClosedOrClosing)
				await _topicBlk.CloseAsync();

			if (_topicTran != null && !_topicTran.IsClosedOrClosing)
				await _topicTran.CloseAsync();
		}

		public ChainProvider ChainProvider
		{
			get; set;
		}
		public BitcoinDWaiters Waiters
		{
			get; set;
		}
	}
}