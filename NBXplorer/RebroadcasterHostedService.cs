using Microsoft.Extensions.Hosting;
using NBitcoin;
using NBXplorer.Backends;
using NBXplorer.Configuration;
using NBXplorer.Events;
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
		public List<Transaction> AlreadyInMempool { get; set; } = new List<Transaction>();
		public List<TrackedTransaction> Cleaned { get; set; } = new List<TrackedTransaction>();

		internal void Add(RebroadcastResult a)
		{
			UnknownFailure.AddRange(a.UnknownFailure);
			AlreadyInMempool.AddRange(a.AlreadyInMempool);
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
		private Indexers _Indexers;
		Dictionary<NBXplorerNetwork, RebroadcastedTransactions> _BroadcastedTransactionsByCryptoCode;
		public RebroadcasterHostedService(
			NBXplorerNetworkProvider networkProvider,
			ExplorerConfiguration configuration, 
			Broadcaster broadcaster,
			RepositoryProvider repositories, Indexers indexers, EventAggregator eventAggregator)
		{
			Broadcaster = broadcaster;
			_Repositories = repositories;
			_Indexers = indexers;
			EventAggregator = eventAggregator;
			_BroadcastedTransactionsByCryptoCode = configuration.ChainConfigurations
													.Select(r => new RebroadcastedTransactions()
													{
														Network = networkProvider.GetFromCryptoCode(r.CryptoCode)
													}).ToDictionary(t => t.Network);
		}

		Task _Loop;
		CancellationTokenSource _Cts = new CancellationTokenSource();

		public Broadcaster Broadcaster { get; }
		public EventAggregator EventAggregator { get; }

		public async Task StartAsync(CancellationToken cancellationToken)
		{
			await _Repositories.StartCompletion;
			_Cts = new CancellationTokenSource();
			_Loop = RebroadcastLoop(_Cts.Token);
		}

		public void RebroadcastPeriodically(NBXplorerNetwork network, TrackedSource trackedSource, params TrackedTransactionKey[] txIds)
		{
			if (network == null)
				throw new ArgumentNullException(nameof(network));
			_BroadcastedTransactionsByCryptoCode[network].RebroadcastPeriodically(trackedSource, txIds);
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
			var rpc = _Indexers.GetIndexer(repository.Network)?.GetConnectedClient();
			if (rpc is null)
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

				var broadcastResult = await Broadcaster.Broadcast(network, tx, trackedTxId.TxId);

				if (broadcastResult.AlreadyInMempool)
				{
					result.AlreadyInMempool.Add(tx);
				}
				if (broadcastResult.Rebroadcasted)
				{
					result.Rebroadcasted.Add(tx);
				}
				if (broadcastResult.UnknownError)
				{
					result.UnknownFailure.Add(tx);
				}

				if (broadcastResult.MissingInput)
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
							var header = await rpc.GetBlockHeaderAsyncEx(savedTx.BlockHash, _Cts.Token);
							if (header is null)
							{
								cleaned.Add(savedTx);
								result.Cleaned.Add(savedTx);
							}
						}
					}
				}
			}

			if (cleaned.Count != 0)
			{
				foreach (var tx in cleaned)
				{
					EventAggregator.Publish(new EvictedTransactionEvent(tx.TransactionHash));
				}
				await repository.Prune(trackedSource, cleaned.ToList());
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
