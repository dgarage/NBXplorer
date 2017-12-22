using NBXplorer.Logging;
using NBXplorer.ModelBinders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.RPC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json.Linq;
using NBXplorer.Events;
using NBXplorer.Configuration;

namespace NBXplorer.Controllers
{
	[Route("v1")]
	[Authorize]
	public class MainController : Controller
	{
		public MainController(RPCClient rpcClient,
			Repository repository,
			ConcurrentChain chain,
			EventAggregator eventAggregator,
			BitcoinDWaiterAccessor initializer,
			NetworkInformation network)
		{
			if(rpcClient == null)
				throw new ArgumentNullException("rpcClient");
			RPC = rpcClient;
			Repository = repository;
			Chain = chain;
			_EventAggregator = eventAggregator;
			Initializer = initializer.Instance;
			_Network = network;
		}

		EventAggregator _EventAggregator;

		public BitcoinDWaiter Initializer
		{
			get; set;
		}

		private NetworkInformation _Network;

		public ConcurrentChain Chain
		{
			get; set;
		}

		public Repository Repository
		{
			get; set;
		}

		public RPCClient RPC
		{
			get; set;
		}

		[HttpGet]
		[Route("fees/{blockCount}")]
		public async Task<GetFeeRateResult> GetFeeRate(int blockCount)
		{
			var result = await RPC.SendCommandAsync("estimatesmartfee", blockCount);
			var feeRateProperty = ((JObject)result.Result).Property("feeRate");
			var rate = feeRateProperty == null ? (decimal)-1 : ((JObject)result.Result)["feerate"].Value<decimal>();
			if(rate == -1)
				throw new NBXplorerError(400, "fee-estimation-unavailable", $"It is currently impossible to estimate fees, please try again later.").AsException();
			return new GetFeeRateResult()
			{
				FeeRate = new FeeRate(Money.Coins(rate), 1000),
				BlockCount = ((JObject)result.Result)["blocks"].Value<int>()
			};
		}

		[HttpPost]
		[Route("subscriptions/blocks")]
		public Task SubscribeToBlocks([FromBody]SubscribeToBlockRequest req)
		{
			return Repository.AddBlockCallback(req.Callback);
		}

		[HttpPost]
		[Route("addresses/{strategy}/subscriptions")]
		public Task SubscribeToWallet(
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase strategy,
			[FromBody]SubscribeToWalletRequest req)
		{
			return Repository.AddCallback(strategy, req.Callback);
		}

		[HttpGet]
		[Route("addresses/{strategy}/unused")]
		public async Task<KeyPathInformation> GetUnusedAddress(
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase strategy, DerivationFeature feature = DerivationFeature.Deposit, int skip = 0, bool reserve = false)
		{
			if(strategy == null)
				throw new ArgumentNullException(nameof(strategy));
			try
			{
				var result = await Repository.GetUnused(strategy, feature, skip, reserve);
				if(result == null)
					throw new NBXplorerError(404, "strategy-not-found", $"This strategy is not tracked, or you tried to skip too much unused addresses").AsException();
				return result;
			}
			catch(NotSupportedException)
			{
				throw new NBXplorerError(400, "derivation-not-supported", $"The derivation scheme {feature} is not supported").AsException();
			}
		}

		[HttpPost]
		[Route("addresses/{strategy}/cancelreservation")]
		public Task CancelReservation([ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase strategy, [FromBody]KeyPath[] keyPaths)
		{
			return Repository.CancelReservation(strategy, keyPaths);
		}

		[HttpGet]
		[Route("status")]
		public async Task<IActionResult> Status()
		{
			var now = DateTimeOffset.UtcNow;

			var blockchainInfoAsync = Initializer.RPCAvailable ? RPC.GetBlockchainInfoAsync() : null;
			await Repository.PingAsync();
			var pingAfter = DateTimeOffset.UtcNow;
			GetBlockchainInfoResponse blockchainInfo = blockchainInfoAsync == null ? null : await blockchainInfoAsync;
			var status = new StatusResult()
			{
				Network = _Network.Network.Name,
				RepositoryPingTime = (DateTimeOffset.UtcNow - now).TotalSeconds,
				IsFullySynched = true
			};

			if(blockchainInfo != null)
			{
				status.BitcoinStatus = new BitcoinStatus()
				{
					IsSynched = !BitcoinDWaiter.IsSynchingCore(blockchainInfo),
					Blocks = blockchainInfo.Blocks,
					Headers = blockchainInfo.Headers,
					VerificationProgress = blockchainInfo.VerificationProgress
				};
				status.IsFullySynched &= status.BitcoinStatus.IsSynched;
			}
			status.ChainHeight = Chain.Height;
			status.IsFullySynched &= blockchainInfo != null
									&& Initializer.State == BitcoinDWaiterState.Ready
									&& blockchainInfo.Headers - Chain.Height < 3;
			return Json(status);
		}

		[HttpGet]
		[Route("tx/{txId}")]
		public IActionResult GetTransaction(
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 txId)
		{
			var result = Repository.GetSavedTransactions(txId);
			if(result.Length == 0)
				return NotFound();

			var tx = result.First().Transaction;

			var confBlock = result
						.Where(r => r.BlockHash != null)
						.Select(r => Chain.GetBlock(r.BlockHash))
						.Where(r => r != null)
						.FirstOrDefault();

			var conf = confBlock == null ? 0 : Chain.Tip.Height - confBlock.Height + 1;

			return new FileContentResult(new TransactionResult() { Confirmations = conf, Transaction = tx }.ToBytes(), "application/octet-stream");
		}

		[HttpPost]
		[Route("track/{derivationStrategy}")]
		public Task TrackWallet(
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase derivationStrategy)
		{
			if(derivationStrategy == null)
				return Task.FromResult(NotFound());
			return Repository.TrackAsync(derivationStrategy);
		}

		[HttpGet]
		[Route("txs/{extPubKey}")]
		public async Task<GetTransactionsResponse> GetTransactions
			([ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase extPubKey,
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 hash = null)
		{
			//var transactions = await GetAnnotatedTransactions(extPubKey);
			return null;
		}

		[HttpGet]
		[Route("sync/{extPubKey}")]
		public async Task<UTXOChanges> Sync(
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase extPubKey,
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 confHash = null,
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 unconfHash = null,
			bool noWait = false)
		{
			if(extPubKey == null)
				throw new ArgumentNullException(nameof(extPubKey));

			var waitingTransaction = noWait ? Task.FromResult(false) : WaitingTransaction(extPubKey);
			UTXOChanges changes = null;
			var getKeyPaths = GetKeyPaths(extPubKey);
			var matchScript = MatchKeyPaths(getKeyPaths);

			while(true)
			{
				changes = new UTXOChanges();
				changes.CurrentHeight = Chain.Height;
				var transactions = GetAnnotatedTransactions(extPubKey);


				var states = UTXOStateResult.CreateStates(matchScript, unconfHash, transactions.UnconfirmedTransactions.Select(c => c.Record.Transaction), confHash, transactions.ConfirmedTransactions.Select(c => c.Record.Transaction));

				changes.Confirmed = SetUTXOChange(states.Confirmed);
				changes.Unconfirmed = SetUTXOChange(states.Unconfirmed, states.Confirmed.Actual);



				FillUTXOsInformation(changes.Confirmed.UTXOs, getKeyPaths, transactions, changes.CurrentHeight);
				FillUTXOsInformation(changes.Unconfirmed.UTXOs, getKeyPaths, transactions, changes.CurrentHeight);

				if(changes.HasChanges || !(await waitingTransaction))
					break;
				waitingTransaction = Task.FromResult(false); //next time, will not wait
			}

			return changes;
		}

		private void CleanConflicts(DerivationStrategyBase extPubKey, AnnotatedTransactionCollection transactions)
		{
			if(transactions.Conflicted.Length != 0)
			{
				foreach(var tx in transactions.Conflicted.Select(c => c.Record))
				{
					_EventAggregator.Publish(new EvictedTransactionEvent(tx.Transaction.GetHash()));
				}
				Repository.CleanTransactions(extPubKey, transactions.Conflicted.Select(c => c.Record).ToList());
			}
		}

		static int[] MaxValue = new[] { int.MaxValue };
		private void FillUTXOsInformation(List<UTXO> utxos, Func<Script[], KeyPath[]> getKeyPaths, AnnotatedTransactionCollection transactionsById, int currentHeight)
		{
			var keyPaths = getKeyPaths(utxos.Select(u => u.ScriptPubKey).ToArray());
			for(int i = 0; i < utxos.Count; i++)
			{
				var utxo = utxos[i];
				utxo.KeyPath = keyPaths[i];
				var txHeight = transactionsById.GetByTxId(utxo.Outpoint.Hash)
									.Select(t => t.Height)
									.Where(h => h.HasValue)
									.Select(t => t.Value)
									.Concat(MaxValue)
									.Min();
				txHeight = txHeight == MaxValue[0] ? currentHeight + 1 : txHeight;
				utxo.Confirmations = currentHeight - txHeight + 1;
			}
		}

		private UTXOChange SetUTXOChange(UTXOStates states, UTXOState substract = null)
		{
			substract = substract ?? new UTXOState();
			var substractedSpent = new HashSet<OutPoint>(substract.SpentUTXOs);
			var substractedReceived = new HashSet<OutPoint>(substract.UTXOByOutpoint.Select(u => u.Key));

			UTXOChange change = new UTXOChange();
			change.Reset = states.Known == null;
			change.Hash = states.Actual.CurrentHash;

			states.Known = states.Known ?? new UTXOState();

			foreach(var coin in states.Actual.UTXOByOutpoint)
			{
				if(!states.Known.UTXOByOutpoint.ContainsKey(coin.Key) &&
					!substractedReceived.Contains(coin.Key))
					change.UTXOs.Add(new UTXO(coin.Value));
			}

			foreach(var outpoint in states.Actual.SpentUTXOs)
			{
				if(!states.Known.SpentUTXOs.Contains(outpoint) &&
					states.Known.UTXOByOutpoint.ContainsKey(outpoint) &&
					!substractedSpent.Contains(outpoint))
					change.SpentOutpoints.Add(outpoint);
			}
			return change;
		}

		private AnnotatedTransactionCollection GetAnnotatedTransactions(DerivationStrategyBase extPubKey)
		{
			var annotatedTransactions = new AnnotatedTransactionCollection(Repository
				.GetTransactions(extPubKey)
				.Select(t => new AnnotatedTransaction(t, Chain))
				.ToList());
			CleanConflicts(extPubKey, annotatedTransactions);
			return annotatedTransactions;
		}

	private async Task<bool> WaitingTransaction(DerivationStrategyBase extPubKey)
	{
		CancellationTokenSource cts = new CancellationTokenSource();
		cts.CancelAfter(10000);

		try
		{
			await _EventAggregator.WaitNext<NewTransactionMatchEvent>(e => e.Match.DerivationStrategy.ToString() == extPubKey.ToString(), cts.Token);
			return true;
		}
		catch(OperationCanceledException) { return false; }
	}

	private Func<Script[], bool[]> MatchKeyPaths(Func<Script[], KeyPath[]> getKeyPaths)
	{
		return (scripts) => getKeyPaths(scripts).Select(c => c != null).ToArray();
	}
	private Func<Script[], KeyPath[]> GetKeyPaths(DerivationStrategyBase extPubKey)
	{
		Dictionary<Script, KeyPath> cache = new Dictionary<Script, KeyPath>();
		return (scripts) =>
		{
			KeyPath[] result = new KeyPath[scripts.Length];
			for(int i = 0; i < result.Length; i++)
			{
				if(cache.TryGetValue(scripts[i], out KeyPath keypath))
					result[i] = keypath;
			}

			var needFetch = scripts.Where((r, i) => result[i] == null).ToArray();
			var fetched = Repository.GetKeyInformations(needFetch).GetAwaiter().GetResult();
			for(int i = 0; i < fetched.Length; i++)
			{
				var keyInfos = fetched[i];
				var script = needFetch[i];
				foreach(var keyInfo in keyInfos)
				{
					if(keyInfo.DerivationStrategy == extPubKey)
					{
						cache.TryAdd(script, keyInfo.KeyPath);
						break;
					}
				}
			}

			for(int i = 0; i < result.Length; i++)
			{
				if(cache.TryGetValue(scripts[i], out KeyPath keypath))
					result[i] = keypath;
			}

			return result;
		};
	}

	[HttpPost]
	[Route("broadcast")]
	public async Task<BroadcastResult> Broadcast(
		[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase extPubKey)
	{
		var tx = new Transaction();
		var stream = new BitcoinStream(Request.Body, false);
		tx.ReadWrite(stream);
		RPCException rpcEx = null;
		try
		{
			await RPC.SendRawTransactionAsync(tx);
			return new BroadcastResult(true);
		}
		catch(RPCException ex)
		{
			rpcEx = ex;
			Logs.Explorer.LogInformation($"Transaction {tx.GetHash()} failed to broadcast (Code: {ex.RPCCode}, Message: {ex.RPCCodeMessage}, Details: {ex.Message} )");
			if(extPubKey != null && ex.Message.StartsWith("Missing inputs", StringComparison.OrdinalIgnoreCase))
			{
				Logs.Explorer.LogInformation("Trying to broadcast unconfirmed of the wallet");
				var transactions = GetAnnotatedTransactions(extPubKey);
				foreach(var existing in transactions.UnconfirmedTransactions)
				{
					try
					{
						await RPC.SendRawTransactionAsync(existing.Record.Transaction);
					}
					catch { }
				}

				try
				{

					await RPC.SendRawTransactionAsync(tx);
					Logs.Explorer.LogInformation($"Broadcast success");
					return new BroadcastResult(true);
				}
				catch(RPCException)
				{
					Logs.Explorer.LogInformation($"Transaction {tx.GetHash()} failed to broadcast (Code: {ex.RPCCode}, Message: {ex.RPCCodeMessage}, Details: {ex.Message} )");
				}
			}
			return new BroadcastResult(false)
			{
				RPCCode = rpcEx.RPCCode,
				RPCCodeMessage = rpcEx.RPCCodeMessage,
				RPCMessage = rpcEx.Message
			};
		}
	}
}
}
