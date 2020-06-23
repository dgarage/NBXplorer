using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.RPC;
using NBXplorer.Events;
using NBXplorer.Logging;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class RebroadcastResult
	{
		public List<Transaction> UnknownFailure { get; set; } = new List<Transaction>();
		public List<Transaction> MissingInputs { get; set; } = new List<Transaction>();
		public List<Transaction> Rebroadcasted { get; set; } = new List<Transaction>();
		public List<TrackedTransaction> Cleaned { get; set; } = new List<TrackedTransaction>();

		internal void Add(RebroadcastResult a)
		{
			UnknownFailure.AddRange(a.UnknownFailure);
			Rebroadcasted.AddRange(a.Rebroadcasted);
			MissingInputs.AddRange(a.MissingInputs);
			Cleaned.AddRange(a.Cleaned);
		}
	}
	public class RebroadcasterHostedService : IHostedService
	{
		class RebroadcastedTransaction
		{
			public RebroadcastedTransaction(TrackedTransactionKey[] keys)
			{
				Keys = new HashSet<TrackedTransactionKey>();
				OrderedKeys = new List<TrackedTransactionKey>();
				AddKeys(keys);
			}

			public void AddKeys(TrackedTransactionKey[] keys)
			{
				// We make sure there is no duplicates, while keeping the order of keys
				foreach (var k in keys)
				{
					if (Keys.Add(k))
						OrderedKeys.Add(k);
				}
			}

			public TrackedSource TrackedSource;
			HashSet<TrackedTransactionKey> Keys;
			public List<TrackedTransactionKey> OrderedKeys;
		}
		class RebroadcastedTransactions
		{
			public NBXplorerNetwork Network;
			object CollectionLock = new object();
			Dictionary<TrackedSource, RebroadcastedTransaction> TransactionsHashSet = new Dictionary<TrackedSource, RebroadcastedTransaction>();
			public void RebroadcastPeriodically(TrackedSource trackedSource, params TrackedTransactionKey[] txIds)
			{
				lock (CollectionLock)
				{
					if (TransactionsHashSet.TryGetValue(trackedSource, out var v))
					{
						v.AddKeys(txIds);
					}
					else
					{
						TransactionsHashSet.Add(trackedSource, new RebroadcastedTransaction(txIds) { TrackedSource = trackedSource });
					}
				}
			}

			public ICollection<RebroadcastedTransaction> RetrieveTransactions()
			{
				lock (CollectionLock)
				{
					var rebroadcasting = TransactionsHashSet.Select(t => t.Value).ToList();
					TransactionsHashSet.Clear();
					return rebroadcasting;
				}
			}
		}

		RepositoryProvider _Repositories;
		private BitcoinDWaiters _Waiters;
		Dictionary<NBXplorerNetwork, RebroadcastedTransactions> _BroadcastedTransactionsByCryptoCode;
		public RebroadcasterHostedService(RepositoryProvider repositories, BitcoinDWaiters waiters, EventAggregator eventAggregator)
		{
			_Repositories = repositories;
			_Waiters = waiters;
			EventAggregator = eventAggregator;
			_BroadcastedTransactionsByCryptoCode = repositories.GetAll()
													.Select(r => new RebroadcastedTransactions()
													{
														Network = r.Network
													}).ToDictionary(t => t.Network);
		}

		Task _Loop;
		CancellationTokenSource _Cts = new CancellationTokenSource();

		public EventAggregator EventAggregator { get; }

		public Task StartAsync(CancellationToken cancellationToken)
		{
			_Cts = new CancellationTokenSource();
			_Loop = RebroadcastLoop(_Cts.Token);
			return Task.CompletedTask;
		}

		public void RebroadcastPeriodically(NBXplorerNetwork network, TrackedSource trackedSource, params TrackedTransactionKey[] txIds)
		{
			if (network == null)
				throw new ArgumentNullException(nameof(network));
			_BroadcastedTransactionsByCryptoCode[network].RebroadcastPeriodically(trackedSource, txIds);
		}

		public async Task RebroadcastPeriodically(NBXplorerNetwork network, TrackedSource trackedSource, params uint256[] txIds)
		{
			List<TrackedTransactionKey> keys = new List<TrackedTransactionKey>();
			foreach (var txId in txIds)
			{
				keys.AddRange((await _Repositories.GetRepository(network).GetTransactions(trackedSource, txId)).Select(k => k.Key));
			}
			RebroadcastPeriodically(network, trackedSource, keys.ToArray());
		}

		private async Task RebroadcastLoop(CancellationToken cancellationToken)
		{
			// Make sure we don't block main thread
			await Task.Delay(1).ConfigureAwait(false);

			while (!cancellationToken.IsCancellationRequested)
			{
				_ = RebroadcastAll();
				await Task.Delay(TimeSpan.FromHours(1.0), cancellationToken);
			}
		}

		public async Task<RebroadcastResult> RebroadcastAll()
		{
			List<Task<RebroadcastResult>> rebroadcast = new List<Task<RebroadcastResult>>();
			foreach (var broadcastedTransactions in _BroadcastedTransactionsByCryptoCode.Select(c => c.Value))
			{
				var txs = broadcastedTransactions.RetrieveTransactions();
				if (txs.Count == 0)
					continue;
				foreach (var tx in txs)
					rebroadcast.Add(Rebroadcast(broadcastedTransactions.Network, tx.TrackedSource, tx.OrderedKeys));
			}
			var result = new RebroadcastResult();
			foreach (var broadcast in rebroadcast)
			{
				result.Add(await broadcast);
			}
			return result;
		}

		private async Task<RebroadcastResult> Rebroadcast(NBXplorerNetwork network, TrackedSource trackedSource, IEnumerable<TrackedTransactionKey> txIds)
		{
			var result = new RebroadcastResult();
			var repository = _Repositories.GetRepository(network);
			var waiter = _Waiters.GetWaiter(repository.Network);
			if (!waiter.RPCAvailable)
				return result;
			List<TrackedTransaction> cleaned = new List<TrackedTransaction>();
			HashSet<TrackedTransactionKey> processedTrackedTransactionKeys = new HashSet<TrackedTransactionKey>();
			HashSet<uint256> processedTransactionId = new HashSet<uint256>();
			foreach (var trackedTxId in txIds)
			{
				if (!processedTransactionId.Add(trackedTxId.TxId))
					continue;
				var tx = (await repository.GetSavedTransactions(trackedTxId.TxId))?.Select(t => t.Transaction).FirstOrDefault();
				if (tx == null)
					continue;
				try
				{
					await waiter.RPC.SendRawTransactionAsync(tx);
					result.Rebroadcasted.Add(tx);
					Logs.Explorer.LogInformation($"{repository.Network.CryptoCode}: Rebroadcasted {trackedTxId.TxId}");
				}
				catch (RPCException ex) when (
				ex.RPCCode == RPCErrorCode.RPC_TRANSACTION_ALREADY_IN_CHAIN ||
				ex.Message.EndsWith("Missing inputs", StringComparison.OrdinalIgnoreCase) ||
				ex.Message.EndsWith("bad-txns-inputs-spent", StringComparison.OrdinalIgnoreCase) ||
				ex.Message.EndsWith("bad-txns-inputs-missingorspent", StringComparison.OrdinalIgnoreCase) ||
				ex.Message.EndsWith("txn-mempool-conflict", StringComparison.OrdinalIgnoreCase))
				{
					result.MissingInputs.Add(tx);
					var txs = await repository.GetTransactions(trackedSource, trackedTxId.TxId);
					foreach (var savedTx in txs)
					{
						if (!processedTrackedTransactionKeys.Add(savedTx.Key))
							continue;
						if (savedTx.BlockHash == null)
						{
							cleaned.Add(savedTx);
							result.Cleaned.Add(savedTx);
						}
						else
						{
							var resultRPC = await waiter.RPC.SendCommandAsync(new RPCRequest("getblockheader", new[] { savedTx.BlockHash }), throwIfRPCError: false);
							if (resultRPC.Error?.Code is RPCErrorCode.RPC_INVALID_ADDRESS_OR_KEY)
							{
								cleaned.Add(savedTx);
								result.Cleaned.Add(savedTx);
							}
							else if (resultRPC.Result.Value<int>("confirmations") == -1)
							{
								cleaned.Add(savedTx);
								result.Cleaned.Add(savedTx);
							}
						}
					}
				}
				catch (Exception ex)
				{
					result.UnknownFailure.Add(tx);
					Logs.Explorer.LogInformation($"{repository.Network.CryptoCode}: Unknown exception when broadcasting {tx.GetHash()} ({ex.Message})");
				}
			}

			if (cleaned.Count != 0)
			{
				foreach (var tx in cleaned)
				{
					EventAggregator.Publish(new EvictedTransactionEvent(tx.TransactionHash));
				}
				await repository.CleanTransactions(trackedSource, cleaned.ToList());
			}
			return result;
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			if (_Loop == null)
				return;
			_Cts.Cancel();
			try
			{
				await _Loop;
			}
			catch
			{

			}
		}
	}
}
