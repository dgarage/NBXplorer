using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer.Backends
{
	public interface IRepository
	{
		int BatchSize { get; set; }
		int MaxPoolSize { get; set; }
		int MinPoolSize { get; set; }
		Money MinUtxoValue { get; set; }
		NBXplorerNetwork Network { get; }
		Serializer Serializer { get; }
		Task Prune(TrackedSource trackedSource, IEnumerable<TrackedTransaction> prunable);
		Task UpdateAddressPool(DerivationSchemeTrackedSource trackedSource, Dictionary<DerivationFeature, int?> highestKeyIndexFound);
		Task CancelReservation(DerivationStrategyBase strategy, KeyPath[] keyPaths);
		TrackedTransaction CreateTrackedTransaction(TrackedSource trackedSource, TrackedTransactionKey transactionKey, IEnumerable<Coin> coins, Dictionary<Script, KeyPath> knownScriptMapping);
		TrackedTransaction CreateTrackedTransaction(TrackedSource trackedSource, TrackedTransactionKey transactionKey, Transaction tx, Dictionary<Script, KeyPath> knownScriptMapping);
		ValueTask<int> DefragmentTables(CancellationToken cancellationToken = default);
		Task<int> GenerateAddresses(DerivationStrategyBase strategy, DerivationFeature derivationFeature, GenerateAddressQuery query = null);
		Task<int> GenerateAddresses(DerivationStrategyBase strategy, DerivationFeature derivationFeature, int maxAddresses);
		Task<IList<NewEventBase>> GetEvents(long lastEventId, int? limit = null);
		Task<BlockLocator> GetIndexProgress();
		Task<MultiValueDictionary<Script, KeyPathInformation>> GetKeyInformations(IList<Script> scripts);
		Task<IList<NewEventBase>> GetLatestEvents(int limit = 10);
		Task<TrackedTransaction[]> GetMatches(Block block, SlimChainedBlock slimBlock, DateTimeOffset now, bool useCache);
		Task<TrackedTransaction[]> GetMatches(IList<Transaction> txs, SlimChainedBlock slimBlock, DateTimeOffset now, bool useCache);
		Task<TrackedTransaction[]> GetMatches(Transaction tx, SlimChainedBlock slimBlock, DateTimeOffset now, bool useCache);
		Task<TMetadata> GetMetadata<TMetadata>(TrackedSource source, string key) where TMetadata : class;
		Task<Dictionary<OutPoint, TxOut>> GetOutPointToTxOut(IList<OutPoint> outPoints);
		Task<SavedTransaction[]> GetSavedTransactions(uint256 txid);
		Task<TrackedTransaction[]> GetTransactions(TrackedSource trackedSource, uint256 txId = null, bool needTx = true, CancellationToken cancellation = default);
		Task<KeyPathInformation> GetUnused(DerivationStrategyBase strategy, DerivationFeature derivationFeature, int n, bool reserve);
		ValueTask<bool> MigrateOutPoints(string directory, CancellationToken cancellationToken = default);
		ValueTask<int> MigrateSavedTransactions(CancellationToken cancellationToken = default);
		Task Ping();
		Task<long> SaveEvent(NewEventBase evt);
		Task SaveKeyInformations(KeyPathInformation[] keyPathInformations);
		Task SaveMatches(TrackedTransaction[] transactions);
		Task SaveMetadata<TMetadata>(TrackedSource source, string key, TMetadata value) where TMetadata : class;
		Task<List<SavedTransaction>> SaveTransactions(DateTimeOffset now, Transaction[] transactions, SlimChainedBlock slimBlock);
		Task SetIndexProgress(BlockLocator locator);
		Task Track(IDestination address);
		ValueTask<int> TrimmingEvents(int maxEvents, CancellationToken cancellationToken = default);
		Task<SlimChainedBlock> GetTip();
		Task SaveBlocks(IList<SlimChainedBlock> slimBlocks);
	}
}