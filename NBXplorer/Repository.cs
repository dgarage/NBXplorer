using Microsoft.Extensions.Logging;
using System.Linq;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System.Threading.Tasks;
using System.Threading;
using NBitcoin.Altcoins;
using NBitcoin.RPC;
using NBXplorer.Logging;
using NBXplorer.Configuration;
using Newtonsoft.Json.Linq;
using static NBXplorer.TrackedTransaction;
using Microsoft.Extensions.Hosting;
using Microsoft.JSInterop;
using System.Buffers;
using DBTrie;
using DBTrie.Storage.Cache;

namespace NBXplorer
{
	public class GenerateAddressQuery
	{
		public GenerateAddressQuery()
		{

		}
		public GenerateAddressQuery(int? minAddresses, int? maxAddresses)
		{
			MinAddresses = minAddresses;
			MaxAddresses = maxAddresses;
		}
		public int? MinAddresses { get; set; }
		public int? MaxAddresses { get; set; }
	}
	public class RepositoryProvider : IHostedService
	{
		DBTrie.DBTrieEngine _Engine;
		Dictionary<string, Repository> _Repositories = new Dictionary<string, Repository>();
		private readonly KeyPathTemplates keyPathTemplates;
		ExplorerConfiguration _Configuration;
		private readonly NBXplorerNetworkProvider networks;

		public RepositoryProvider(NBXplorerNetworkProvider networks, KeyPathTemplates keyPathTemplates, ExplorerConfiguration configuration)
		{
			this.keyPathTemplates = keyPathTemplates;
			_Configuration = configuration;
			this.networks = networks;
		}

		private ChainConfiguration GetChainSetting(NBXplorerNetwork net)
		{
			return _Configuration.ChainConfigurations.FirstOrDefault(c => c.CryptoCode == net.CryptoCode);
		}

		public Repository GetRepository(string cryptoCode)
		{
			_Repositories.TryGetValue(cryptoCode, out Repository repository);
			return repository;
		}
		public Repository GetRepository(NBXplorerNetwork network)
		{
			return GetRepository(network.CryptoCode);
		}

		TaskCompletionSource<bool> _StartCompletion = new TaskCompletionSource<bool>();
		public Task StartCompletion => _StartCompletion.Task;
		public async Task StartAsync(CancellationToken cancellationToken)
		{
			try
			{
				var directory = Path.Combine(_Configuration.DataDir, "db");
				if (!Directory.Exists(directory))
					Directory.CreateDirectory(directory);

				_Engine = await OpenEngine(directory, cancellationToken);
				if (_Configuration.DBCache > 0)
				{
					int pageSize = 8192;
					_Engine.ConfigurePagePool(new PagePool(pageSize, (_Configuration.DBCache * 1000 * 1000) / pageSize));
				}
				foreach (var net in networks.GetAll())
				{
					var settings = GetChainSetting(net);
					if (settings != null)
					{
						var repo = net.NBitcoinNetwork.NetworkSet == Liquid.Instance ? new LiquidRepository(_Engine, net, keyPathTemplates, settings.RPC) : new Repository(_Engine, net, keyPathTemplates, settings.RPC);
						repo.MaxPoolSize = _Configuration.MaxGapSize;
						repo.MinPoolSize = _Configuration.MinGapSize;
						repo.MinUtxoValue = settings.MinUtxoValue;
						_Repositories.Add(net.CryptoCode, repo);
					}
				}
				foreach (var repo in _Repositories.Select(kv => kv.Value))
				{
					if (GetChainSetting(repo.Network) is ChainConfiguration chainConf && chainConf.Rescan)
					{
						Logs.Configuration.LogInformation($"{repo.Network.CryptoCode}: Rescanning the chain...");
						await repo.SetIndexProgress(null);
					}
				}

				Logs.Explorer.LogInformation("Defragmenting transaction tables...");
				int saved = 0;
				var defragFile = Path.Combine(Path.Combine(_Configuration.DataDir), "defrag-lock");
				if (!File.Exists(defragFile))
				{
					File.Create(defragFile).Close();
					foreach (var repo in _Repositories.Select(kv => kv.Value))
					{

						if (GetChainSetting(repo.Network) is ChainConfiguration chainConf)
						{
							saved += await repo.DefragmentTables(cancellationToken);
						}
						if (File.Exists(defragFile))
							File.Delete(defragFile);
					}
					Logs.Explorer.LogInformation($"Defragmentation succeed, {PrettyKB(saved)} saved");
				}
				else
				{
					Logs.Explorer.LogWarning($"Defragmentation skipped, it seems to have crashed your NBXplorer before. (file {defragFile} already exists)");
				}

				Logs.Explorer.LogInformation("Applying migration if needed, do not close NBXplorer...");
				int migrated = 0;
				foreach (var repo in _Repositories.Select(kv => kv.Value))
				{
					if (GetChainSetting(repo.Network) is ChainConfiguration chainConf)
					{
						migrated += await repo.MigrateSavedTransactions(cancellationToken);
					}
				}
				if (migrated != 0)
					Logs.Explorer.LogInformation($"Migrated {migrated} tables...");

				if (_Configuration.TrimEvents > 0)
				{
					Logs.Explorer.LogInformation("Trimming the event table if needed...");
					int trimmed = 0;
					foreach (var repo in _Repositories.Select(kv => kv.Value))
					{
						if (GetChainSetting(repo.Network) is ChainConfiguration chainConf)
						{
							trimmed += await repo.TrimmingEvents(_Configuration.TrimEvents, cancellationToken);
						}
					}
					if (trimmed != 0)
						Logs.Explorer.LogInformation($"Trimmed {trimmed} events in total...");
				}
				_StartCompletion.TrySetResult(true);
			}
			catch
			{
				_StartCompletion.TrySetCanceled();
				throw;
			}
		}

		private string PrettyKB(int bytes)
		{
			if (bytes < 1024)
				return $"{bytes} bytes";
			if (bytes / 1024 < 1024)
				return $"{Math.Round((double)bytes / 1024.0, 2):0.00} kB";
			return $"{Math.Round((double)bytes / 1024.0 / 1024.0, 2):0.00} MB";
		}

		private async ValueTask<DBTrieEngine> OpenEngine(string directory, CancellationToken cancellationToken)
		{
			int tried = 0;
		retry:
			try
			{
				return await DBTrie.DBTrieEngine.OpenFromFolder(directory);
			}
			catch when (tried < 10)
			{
				tried++;
				await Task.Delay(500, cancellationToken);
				goto retry;
			}
			catch
			{
				throw;
			}
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			if (_Engine is null)
				return;
			await _Engine.DisposeAsync();
		}
	}

	public class Repository
	{


		public async Task Ping()
		{
			using var tx = await engine.OpenTransaction();
		}

		public async Task CancelReservation(DerivationStrategyBase strategy, KeyPath[] keyPaths)
		{
			if (keyPaths.Length == 0)
				return;
			using var tx = await engine.OpenTransaction();
			bool needCommit = false;
			var featuresPerKeyPaths = keyPathTemplates.GetSupportedDerivationFeatures()
			.Select(f => (Feature: f, KeyPathTemplate: keyPathTemplates.GetKeyPathTemplate(f)))
			.ToDictionary(o => o.KeyPathTemplate, o => o.Feature);

			var groups = keyPaths.Where(k => k.Indexes.Length > 0).GroupBy(k => keyPathTemplates.GetKeyPathTemplate(k));
			foreach (var group in groups)
			{
				if (featuresPerKeyPaths.TryGetValue(group.Key, out DerivationFeature feature))
				{
					var reserved = GetReservedKeysIndex(tx, strategy, feature);
					var available = GetAvailableKeysIndex(tx, strategy, feature);
					foreach (var keyPath in group)
					{
						var key = (int)keyPath.Indexes.Last();
						using var data = await reserved.SelectBytes(key);
						if (data == null)
							continue;
						await reserved.RemoveKey(data.Key);
						await available.Insert(data.Key, await data.ReadValue());
						needCommit = true;
					}
				}
			}
			if (needCommit)
				await tx.Commit();
		}

		class Index
		{
			public DBTrie.Table table;
			public Index(DBTrie.Transaction tx, string tableName, string primaryKey)
			{
				PrimaryKey = primaryKey;
				this.table = tx.GetTable(tableName);
			}

			public string PrimaryKey
			{
				get; set;
			}

			public ValueTask<DBTrie.IRow> SelectBytes(int index)
			{
				return table.Get($"{PrimaryKey}-{index:D10}");
			}
			public async ValueTask<bool> RemoveKey(string key)
			{
				return await table.Delete($"{PrimaryKey}-{key}");
			}
			public async ValueTask RemoveKey(ReadOnlyMemory<byte> key)
			{
				await table.Delete(key);
			}

			public async ValueTask Insert(int index, ReadOnlyMemory<byte> value)
			{
				await table.Insert($"{index:D10}", value);
			}
			public async ValueTask Insert(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value)
			{
				await (table).Insert(key, value);
			}

			public async IAsyncEnumerable<DBTrie.IRow> SelectForwardSkip(int n, string startWith = null, EnumerationOrder order = EnumerationOrder.Ordered)
			{
				if (startWith == null)
					startWith = PrimaryKey;
				else
					startWith = $"{PrimaryKey}-{startWith}";
				int skipped = 0;
				await foreach (var row in table.Enumerate(startWith, order))
				{
					if (skipped < n)
					{
						row.Dispose();
						skipped++;
					}
					else
						yield return row;
				}
			}

			public async IAsyncEnumerable<DBTrie.IRow> SelectFrom(long key, int? limit)
			{
				var remaining = limit is int l ? l : int.MaxValue;
				if (remaining is 0)
					yield break;
				await foreach (var row in EnumerateFromKey(table, $"{PrimaryKey}-{key:D20}"))
				{
					yield return row;
					remaining--;
					if (remaining is 0)
						yield break;
				}
			}

			private async IAsyncEnumerable<IRow> EnumerateFromKey(Table table, string key)
			{
				var thisKey = Encoding.UTF8.GetBytes(key).AsMemory();
				bool returns = false;
				await foreach (var row in table.Enumerate())
				{
					if (returns)
					{
						yield return row;
					}
					else
					{
						var comparison = thisKey.Span.SequenceCompareTo(row.Key.Span);
						if (comparison <= 0)
						{
							returns = true;
						}
						if (comparison < 0)
						{
							yield return row;
						}
						else
						{
							row.Dispose();
						}
					}
				}
			}

			public async Task<int> Count()
			{
				int count = 0;
				await foreach (var item in table.Enumerate(PrimaryKey))
				{
					using (item)
					{
						count++;
					}
				}
				return count;
			}

			public async ValueTask Insert(int key, byte[] value)
			{
				await Insert($"{key:D10}", value);
			}
			public async ValueTask Insert(long key, byte[] value)
			{
				await Insert($"{key:D20}", value);
			}
			public async ValueTask Insert(string key, byte[] value, bool replace = true)
			{
				await table.Insert($"{PrimaryKey}-{key}", value, replace);
			}
			public async Task<int?> SelectInt(int index)
			{
				using var row = await SelectBytes(index);
				if (row == null)
					return null;
				var v = await row.ReadValue();
				uint value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(v.Span);
				value = value & ~0x8000_0000U;
				return (int)value;
			}
			public async Task<long?> SelectLong(int index)
			{
				using var row = await SelectBytes(index);
				if (row == null)
					return null;
				var v = await row.ReadValue();
				ulong value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(v.Span);
				value = value & ~0x8000_0000_0000_0000UL;
				return (long)value;
			}
			public async ValueTask Insert(int key, int value)
			{
				var bytes = NBitcoin.Utils.ToBytes((uint)value, false);
				await Insert(key, bytes);
			}
			public async ValueTask Insert(int key, long value)
			{
				var bytes = NBitcoin.Utils.ToBytes((ulong)value, false);
				await Insert(key, bytes);
			}
		}

		Index GetAvailableKeysIndex(DBTrie.Transaction tx, DerivationStrategyBase trackedSource, DerivationFeature feature)
		{
			return new Index(tx, $"{_Suffix}AvailableKeys", $"{trackedSource.GetHash()}-{feature}");
		}

		Index GetScriptsIndex(DBTrie.Transaction tx, Script scriptPubKey)
		{
			return new Index(tx, $"{_Suffix}Scripts", $"{scriptPubKey.Hash}");
		}

		Index GetOutPointsIndex(DBTrie.Transaction tx, OutPoint outPoint)
		{
			return new Index(tx, $"{_Suffix}OutPoints", $"{outPoint}");
		}
		Index GetHighestPathIndex(DBTrie.Transaction tx, DerivationStrategyBase strategy, DerivationFeature feature)
		{
			return new Index(tx, $"{_Suffix}HighestPath", $"{strategy.GetHash()}-{feature}");
		}

		Index GetReservedKeysIndex(DBTrie.Transaction tx, DerivationStrategyBase strategy, DerivationFeature feature)
		{
			return new Index(tx, $"{_Suffix}ReservedKeys", $"{strategy.GetHash()}-{feature}");
		}

		Index GetTransactionsIndex(DBTrie.Transaction tx, TrackedSource trackedSource)
		{
			return new Index(tx, $"{_Suffix}Transactions", $"{trackedSource.GetHash()}");
		}

		Index GetMetadataIndex(DBTrie.Transaction tx, TrackedSource trackedSource)
		{
			return new Index(tx, $"{_Suffix}Metadata", $"{trackedSource.GetHash()}");
		}

		Index GetEventsIndex(DBTrie.Transaction tx)
		{
			return new Index(tx, $"{_Suffix}Events", string.Empty);
		}

		protected NBXplorerNetwork _Network;
		private readonly KeyPathTemplates keyPathTemplates;
		private readonly RPCClient rpc;

		public NBXplorerNetwork Network
		{
			get
			{
				return _Network;
			}
		}

		DBTrie.DBTrieEngine engine;
		internal Repository(DBTrie.DBTrieEngine engine, NBXplorerNetwork network, KeyPathTemplates keyPathTemplates, RPCClient rpc)
		{
			if (network == null)
				throw new ArgumentNullException(nameof(network));
			_Network = network;
			this.keyPathTemplates = keyPathTemplates;
			this.rpc = rpc;
			Serializer = new Serializer(_Network);
			_Network = network;
			this.engine = engine;
			_Suffix = network.CryptoCode == "BTC" ? "" : network.CryptoCode;
		}

		public string _Suffix;
		public async Task<BlockLocator> GetIndexProgress()
		{
			using var tx = await engine.OpenTransaction();
			using var existingRow = await tx.GetTable($"{_Suffix}IndexProgress").Get("");
			if (existingRow == null)
				return null;
			BlockLocator locator = new BlockLocator();
			locator.FromBytes(DBTrie.PublicExtensions.GetUnderlyingArraySegment(await existingRow.ReadValue()).Array);
			return locator;
		}

		public async Task SetIndexProgress(BlockLocator locator)
		{
			using var tx = await engine.OpenTransaction();
			if (locator == null)
				await tx.GetTable($"{_Suffix}IndexProgress").Delete(string.Empty);
			else
				await tx.GetTable($"{_Suffix}IndexProgress").Insert(string.Empty, locator.ToBytes());
			await tx.Commit();
		}

		public async Task<KeyPathInformation> GetUnused(DerivationStrategyBase strategy, DerivationFeature derivationFeature, int n, bool reserve)
		{
			KeyPathInformation keyInfo = null;
			using (var tx = await engine.OpenTransaction())
			{
				var availableTable = GetAvailableKeysIndex(tx, strategy, derivationFeature);
				var reservedTable = GetReservedKeysIndex(tx, strategy, derivationFeature);
				var rows = availableTable.SelectForwardSkip(n);
				await using var enumerator = rows.GetAsyncEnumerator();
				if (!await enumerator.MoveNextAsync())
				{
					await enumerator.DisposeAsync();
					return null;
				}
				using var row = enumerator.Current;
				await enumerator.DisposeAsync();
				keyInfo = ToObject<KeyPathInformation>(await row.ReadValue()).AddAddress(Network.NBitcoinNetwork);
				if (reserve)
				{
					await availableTable.RemoveKey(row.Key);
					await reservedTable.Insert(row.Key, await row.ReadValue());
					await tx.Commit();
				}
			}
			if (keyInfo != null)
			{
				await ImportAddressToRPC(keyInfo.TrackedSource, keyInfo.Address, keyInfo.KeyPath);
			}
			return keyInfo;
		}

		async Task<int> GetAddressToGenerateCount(DBTrie.Transaction tx, DerivationStrategyBase strategy, DerivationFeature derivationFeature)
		{
			var availableTable = GetAvailableKeysIndex(tx, strategy, derivationFeature);
			var currentlyAvailable = await availableTable.Count();
			if (currentlyAvailable >= MinPoolSize)
				return 0;
			return Math.Max(0, MaxPoolSize - currentlyAvailable);
		}

		private async ValueTask RefillAvailable(DBTrie.Transaction tx, DerivationStrategyBase strategy, DerivationFeature derivationFeature, int toGenerate)
		{
			if (toGenerate <= 0)
				return;
			var availableTable = GetAvailableKeysIndex(tx, strategy, derivationFeature);
			var highestTable = GetHighestPathIndex(tx, strategy, derivationFeature);
			int highestGenerated = (await highestTable.SelectInt(0)) ?? -1;
			var feature = strategy.GetLineFor(keyPathTemplates.GetKeyPathTemplate(derivationFeature));

			KeyPathInformation[] keyPathInformations = new KeyPathInformation[toGenerate];
			Parallel.For(0, toGenerate, i =>
			{
				var index = highestGenerated + i + 1;
				var derivation = feature.Derive((uint)index);
				var info = new KeyPathInformation(derivation,
					new DerivationSchemeTrackedSource(strategy),
					derivationFeature,
					keyPathTemplates.GetKeyPathTemplate(derivationFeature).GetKeyPath(index, false),
					Network);
				keyPathInformations[i] = info;
			});
			for (int i = 0; i < toGenerate; i++)
			{
				var index = highestGenerated + i + 1;
				var info = keyPathInformations[i];
				var bytes = ToBytes(info);
				await GetScriptsIndex(tx, info.ScriptPubKey).Insert($"{strategy.GetHash()}-{derivationFeature}", bytes);
				await availableTable.Insert(index, bytes);
			}
			await highestTable.Insert(0, highestGenerated + toGenerate);
			await tx.Commit();
		}

		public async Task<long> SaveEvent(NewEventBase evt)
		{
			// Fetch the lastEventId on row 0
			// Increment it,
			// Insert event
			using var tx = await engine.OpenTransaction();
			var idx = GetEventsIndex(tx);
			var lastEventIndexMaybe = await idx.SelectLong(0);
			var lastEventIndex = lastEventIndexMaybe.HasValue ? lastEventIndexMaybe.Value + 1 : 1;
			await idx.Insert(0, lastEventIndex);
			await idx.Insert(lastEventIndex, this.ToBytes(evt.ToJObject(Serializer.Settings)));
			await tx.Commit();
			lastKnownEventIndex = lastEventIndex;
			return lastEventIndex;
		}
		long lastKnownEventIndex = -1;
		public async Task<IList<NewEventBase>> GetLatestEvents(int limit = 10)
		{
			using var tx = await engine.OpenTransaction();
			if (limit < 1)
				return new List<NewEventBase>();
			// Find the last event id
			var idx = GetEventsIndex(tx);
			var lastEventIndexMaybe = await idx.SelectLong(0);
			var lastEventIndex = lastEventIndexMaybe.HasValue ? lastEventIndexMaybe.Value : lastKnownEventIndex;
			// If less than 1, no events exist
			if (lastEventIndex < 1)
				return new List<NewEventBase>();
			// Find where we want to start selecting
			// smallest value ex. limit = 1, lastEventIndex = 1
			// 1 - 1 = 0 So we will never select index 0 (Which stores last index)
			var startId = lastEventIndex - limit;
			// Event must exist since lastEventIndex >= 1, set minimum to 0.
			var lastEventId = startId < 0 ? 0 : startId;
			// SelectFrom returns the first event after lastEventId
			// to lastEventId = 0 means it will select starting from the first event
			var query = idx.SelectFrom(lastEventId, limit);
			IList<NewEventBase> evts = new List<NewEventBase>();
			await foreach (var value in query)
			{
				using (value)
				{
					var id = ExtractLong(value.Key);
					var evt = NewEventBase.ParseEvent(ToObject<JObject>(await value.ReadValue()), Serializer.Settings);
					evt.EventId = id;
					evts.Add(evt);
				}
			}
			return evts;
		}

		private long ExtractLong(ReadOnlyMemory<byte> key)
		{
			var span = Encoding.UTF8.GetString(key.Span).AsSpan();
			var sep = span.LastIndexOf('-');
			span = span.Slice(sep + 1);
			return long.Parse(span);
		}

		public async Task<IList<NewEventBase>> GetEvents(long lastEventId, int? limit = null)
		{
			if (lastEventId < 1 && limit.HasValue && limit.Value != int.MaxValue)
				limit = limit.Value + 1; // The row with key 0 holds the lastEventId
			using var tx = await engine.OpenTransaction();
			if (lastKnownEventIndex != -1 && lastKnownEventIndex == lastEventId)
				return new List<NewEventBase>();
			var idx = GetEventsIndex(tx);
			var query = idx.SelectFrom(lastEventId, limit);
			IList<NewEventBase> evts = new List<NewEventBase>();
			await foreach (var value in query)
			{
				using (value)
				{
					var id = ExtractLong(value.Key);
					if (id == 0) // Last Index
						continue;
					var evt = NewEventBase.ParseEvent(ToObject<JObject>(await value.ReadValue()), Serializer.Settings);
					evt.EventId = id;
					evts.Add(evt);
				}
			}
			return evts;
		}

		public async Task SaveKeyInformations(KeyPathInformation[] keyPathInformations)
		{
			using var tx = await engine.OpenTransaction();
			foreach (var info in keyPathInformations)
			{
				var bytes = ToBytes(info);
				await GetScriptsIndex(tx, info.ScriptPubKey).Insert($"{info.DerivationStrategy.GetHash()}-{info.Feature}", bytes);
			}
			await tx.Commit();
		}

		public async Task SaveOutPointToScript((OutPoint outPoint, Script script)[] mapping)
		{
			using var tx = await engine.OpenTransaction();
			foreach (var (outPoint, script) in mapping)
			{
				var bytes = ToBytes(script);
				await GetOutPointsIndex(tx, outPoint).Insert(0, bytes);
			}
			await tx.Commit();
		}
		public async Task<Dictionary<OutPoint, Script>> GetOutPointToScript(OutPoint[] outPoints)
		{
			var result = new Dictionary<OutPoint, Script>();
			if (outPoints.Length == 0)
				return result;
			foreach (var batch in outPoints.Batch(BatchSize))
			{
				using var tx = await engine.OpenTransaction();
				foreach (var outPoint in batch)
				{
					var table = GetOutPointsIndex(tx, outPoint);
					var rawScript = await table.SelectBytes(0);
					if (rawScript is null)
						result.Add(outPoint, null);
					else
					{
						var rawScriptAsByte = await rawScript.ReadValue();
						result[outPoint] = ToObject<Script>(rawScriptAsByte);
					}
				}
			}
			return result;
		}

		public async Task Track(IDestination address)
		{
			using var tx = await engine.OpenTransaction();
			var info = new KeyPathInformation()
			{
				ScriptPubKey = address.ScriptPubKey,
				TrackedSource = (TrackedSource)address,
				Address = (address as BitcoinAddress) ?? address.ScriptPubKey.GetDestinationAddress(Network.NBitcoinNetwork)
			};
			var bytes = ToBytes(info);
			await GetScriptsIndex(tx, address.ScriptPubKey).Insert(address.ScriptPubKey.Hash.ToString(), bytes);
			await tx.Commit();
		}

		public Task<int> GenerateAddresses(DerivationStrategyBase strategy, DerivationFeature derivationFeature, int maxAddresses)
		{
			return GenerateAddresses(strategy, derivationFeature, new GenerateAddressQuery(null, maxAddresses));
		}
		public async Task<int> GenerateAddresses(DerivationStrategyBase strategy, DerivationFeature derivationFeature, GenerateAddressQuery query = null)
		{
			query = query ?? new GenerateAddressQuery();
			using var tx = await engine.OpenTransaction();
			var toGenerate = await GetAddressToGenerateCount(tx, strategy, derivationFeature);
			if (query.MaxAddresses is int max)
				toGenerate = Math.Min(max, toGenerate);
			if (query.MinAddresses is int min)
				toGenerate = Math.Max(min, toGenerate);
			await RefillAvailable(tx, strategy, derivationFeature, toGenerate);
			return toGenerate;
		}

		class TimeStampedTransaction : IBitcoinSerializable
		{

			public TimeStampedTransaction()
			{

			}
			public TimeStampedTransaction(Network network, ReadOnlyMemory<byte> hex)
			{
				var segment = DBTrie.PublicExtensions.GetUnderlyingArraySegment(hex);
				var stream = new BitcoinStream(segment.Array, segment.Offset, segment.Count);
				stream.ConsensusFactory = network.Consensus.ConsensusFactory;
				this.ReadWrite(stream);
			}

			public TimeStampedTransaction(NBitcoin.Transaction tx, ulong timestamp)
			{
				_TimeStamp = timestamp;
				_Transaction = tx;
			}
			NBitcoin.Transaction _Transaction;
			public NBitcoin.Transaction Transaction
			{
				get
				{
					return _Transaction;
				}
				set
				{
					_Transaction = value;
				}
			}


			ulong _TimeStamp = 0;
			public ulong TimeStamp
			{
				get
				{
					return _TimeStamp;
				}
				set
				{
					_TimeStamp = value;
				}
			}

			public void ReadWrite(BitcoinStream stream)
			{
				stream.ReadWrite(ref _Transaction);

				// So we don't crash on transactions indexed on old versions
				if (stream.Serializing || stream.Inner.Position != stream.Inner.Length)
					stream.ReadWrite(ref _TimeStamp);
			}
		}

		public int BatchSize
		{
			get; set;
		} = 100;
		public async Task<List<SavedTransaction>> SaveTransactions(DateTimeOffset now, NBitcoin.Transaction[] transactions, uint256 blockHash)
		{
			var result = new List<SavedTransaction>();
			transactions = transactions.Distinct().ToArray();
			var outPointToPubScript = new List<(OutPoint, Script)>();
			var scripts = new HashSet<Script>();
			if (transactions.Length == 0)
				return result;
			foreach (var batch in transactions.Batch(BatchSize))
			{
				using var tx = await engine.OpenTransaction();
				var date = NBitcoin.Utils.DateTimeToUnixTime(now);
				foreach (var btx in batch)
				{
					foreach (var coin in btx.Outputs.AsCoins())
					{
						outPointToPubScript.Add((coin.Outpoint, coin.ScriptPubKey));
					}
					var timestamped = new TimeStampedTransaction(btx, date);
					var value = timestamped.ToBytes();
					var key = GetSavedTransactionKey(btx.GetHash(), blockHash);
					await GetSavedTransactionTable(tx).Insert(key, value);
					result.Add(ToSavedTransaction(Network.NBitcoinNetwork, key, value));
				}
				await tx.Commit();
			}

			await SaveOutPointToScript(outPointToPubScript.ToArray());

			return result;
		}

		public class SavedTransaction
		{
			public NBitcoin.Transaction Transaction
			{
				get; set;
			}
			public uint256 BlockHash
			{
				get; set;
			}
			public DateTimeOffset Timestamp
			{
				get;
				set;
			}
		}

		public async Task<SavedTransaction[]> GetSavedTransactions(uint256 txid)
		{
			List<SavedTransaction> saved = new List<SavedTransaction>();
			using var tx = await engine.OpenTransaction();
			var table = GetSavedTransactionTable(tx);
			await foreach (var row in table.Enumerate(GetSavedTransactionKey(txid, null)))
			{
				using (row)
				{
					SavedTransaction t = ToSavedTransaction(Network.NBitcoinNetwork, row.Key, await row.ReadValue());
					saved.Add(t);
				}
			}
			return saved.ToArray();
		}

		private static SavedTransaction ToSavedTransaction(Network network, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value)
		{
			SavedTransaction t = new SavedTransaction();
			if (key.Length > 32)
			{
				t.BlockHash = new uint256(key.Span.Slice(32));
			}
			var timeStamped = new TimeStampedTransaction(network, value);
			t.Transaction = timeStamped.Transaction;
			t.Timestamp = NBitcoin.Utils.UnixTimeToDateTime(timeStamped.TimeStamp);
			t.Transaction.PrecomputeHash(true, false);
			return t;
		}
		public async Task<MultiValueDictionary<OutPoint, KeyPathInformation>> GetKeyInformations(OutPoint[] outPoints)
		{
			MultiValueDictionary<OutPoint, KeyPathInformation> result = new MultiValueDictionary<OutPoint, KeyPathInformation>();
			if (outPoints.Length == 0)
				return result;
			var outPointToScript = await GetOutPointToScript(outPoints);
			var scriptToKeyPaths = await GetKeyInformations(outPointToScript.Select(kv => kv.Value).Where(s => !(s is null)).ToArray());
			foreach (var (outPoint, script) in outPointToScript)
			{
				if (!(script is null))
					result.AddRange(outPoint, scriptToKeyPaths[script]);
			}
			return result;
		}
		public async Task<MultiValueDictionary<OutPoint, KeyPathInformation>> GetKeyInformations((OutPoint outPoint, Script script)[] outPointWithScript)
		{

			await SaveOutPointToScript(outPointWithScript.Where(o => !(o.script is null)).ToArray());
			return await GetKeyInformations(outPointWithScript.Select(o => o.outPoint).ToArray());
		}
		public async Task<MultiValueDictionary<Script, KeyPathInformation>> GetKeyInformations(Script[] scripts)
		{
			MultiValueDictionary<Script, KeyPathInformation> result = new MultiValueDictionary<Script, KeyPathInformation>();
			if (scripts.Length == 0)
				return result;
			foreach (var batch in scripts.Batch(BatchSize))
			{
				using var tx = await engine.OpenTransaction();
				foreach (var script in batch)
				{
					var table = GetScriptsIndex(tx, script);
					var keyInfos = new List<KeyPathInformation>();
					await foreach (var row in table.SelectForwardSkip(0))
					{
						using (row)
						{
							var keyInfo = ToObject<KeyPathInformation>(await row.ReadValue())
											.AddAddress(Network.NBitcoinNetwork);

							// Because xpub are mutable (several xpub map to same script)
							// an attacker could generate lot's of xpub mapping to the same script
							// and this would blow up here. This we take only 5 results max.
							keyInfos.Add(keyInfo);
							if (keyInfos.Count == 5)
								break;
						}
					}
					result.AddRange(script, keyInfos);
				}
			}
			return result;
		}

		public Serializer Serializer
		{
			get; private set;
		}

		private T ToObject<T>(ReadOnlyMemory<byte> value)
		{
			var result = Serializer.ToObject<T>(Unzip(value));

			// For back compat, some old serialized KeyPathInformation do not have TrackedSource property set
			if (result is KeyPathInformation keyPathInfo)
			{
				if (keyPathInfo.TrackedSource == null && keyPathInfo.DerivationStrategy != null)
				{
					keyPathInfo.TrackedSource = new DerivationSchemeTrackedSource(keyPathInfo.DerivationStrategy);
				}
			}
			return result;
		}
		private byte[] ToBytes<T>(T obj)
		{
			return Zip(Serializer.ToString<T>(obj));
		}

		private byte[] Zip(string unzipped)
		{
			MemoryStream ms = new MemoryStream();
			using (GZipStream gzip = new GZipStream(ms, CompressionMode.Compress))
			{
				StreamWriter writer = new StreamWriter(gzip, Encoding.UTF8);
				writer.Write(unzipped);
				writer.Flush();
			}
			return ms.ToArray();
		}
		private string Unzip(ReadOnlyMemory<byte> bytes)
		{
			var segment = DBTrie.PublicExtensions.GetUnderlyingArraySegment(bytes);
			MemoryStream ms = new MemoryStream(segment.Array, segment.Offset, segment.Count);
			using (GZipStream gzip = new GZipStream(ms, CompressionMode.Decompress))
			{
				StreamReader reader = new StreamReader(gzip, Encoding.UTF8);
				var unzipped = reader.ReadToEnd();
				return unzipped;
			}
		}

		public int MinPoolSize
		{
			get; set;
		} = 20;
		public int MaxPoolSize
		{
			get; set;
		} = 30;
		public Money MinUtxoValue
		{
			get; set;
		} = Money.Satoshis(1);

		public async Task<TrackedTransaction[]> GetTransactions(TrackedSource trackedSource, uint256 txId = null, CancellationToken cancellation = default)
		{
			Dictionary<uint256, long> firstSeenList = new Dictionary<uint256, long>();
			HashSet<ITrackedTransactionSerializable> needRemove = new HashSet<ITrackedTransactionSerializable>();
			HashSet<ITrackedTransactionSerializable> needUpdate = new HashSet<ITrackedTransactionSerializable>();

			var transactions = new List<ITrackedTransactionSerializable>();
			using (var tx = await engine.OpenTransaction(cancellation))
			{
				var table = GetTransactionsIndex(tx, trackedSource);

				await foreach (var row in table.SelectForwardSkip(0, txId?.ToString(), EnumerationOrder.Unordered))
				{
					using (row)
					{
						cancellation.ThrowIfCancellationRequested();
						var seg = DBTrie.PublicExtensions.GetUnderlyingArraySegment(await row.ReadValue());
						MemoryStream ms = new MemoryStream(seg.Array, seg.Offset, seg.Count);
						BitcoinStream bs = new BitcoinStream(ms, false);
						bs.ConsensusFactory = Network.NBitcoinNetwork.Consensus.ConsensusFactory;
						var data = CreateBitcoinSerializableTrackedTransaction(TrackedTransactionKey.Parse(row.Key.Span));
						data.ReadWrite(bs);
						transactions.Add(data);
						long firstSeen;
						if (firstSeenList.TryGetValue(data.Key.TxId, out firstSeen))
						{
							if (firstSeen > data.FirstSeenTickCount)
								firstSeenList[data.Key.TxId] = firstSeen;
						}
						else
						{
							firstSeenList.Add(data.Key.TxId, data.FirstSeenTickCount);
						}
					}
				}
			}

			ITrackedTransactionSerializable previousConfirmed = null;
			foreach (var tx in transactions)
			{
				if (tx.Key.BlockHash != null)
				{
					if (!tx.Key.IsPruned)
					{
						previousConfirmed = tx;
					}
					else if (previousConfirmed != null &&
							 tx.Key.TxId == previousConfirmed.Key.TxId &&
							 tx.Key.BlockHash == previousConfirmed.Key.BlockHash)
					{
						needRemove.Add(tx);

						foreach (var kv in tx.KnownKeyPathMapping)
						{
							// The pruned transaction has more info about owned utxo than the unpruned, we need to update the unpruned
							if (previousConfirmed.KnownKeyPathMapping.TryAdd(kv.Key, kv.Value))
								needUpdate.Add(previousConfirmed);
						}
					}
					else
					{
						previousConfirmed = null;
					}
				}

				if (tx.FirstSeenTickCount != firstSeenList[tx.Key.TxId])
				{
					needUpdate.Add(tx);
					tx.FirstSeenTickCount = firstSeenList[tx.Key.TxId];
				}
			}
			if (needUpdate.Count != 0 || needRemove.Count != 0)
			{
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
				Cleanup(trackedSource, needRemove, needUpdate);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
			}
			return transactions.Where(tt => !needRemove.Contains(tt)).Select(c => ToTrackedTransaction(c, trackedSource)).ToArray();
		}

		private async Task Cleanup(TrackedSource trackedSource, HashSet<ITrackedTransactionSerializable> needRemove, HashSet<ITrackedTransactionSerializable> needUpdate)
		{
			using (var tx = await engine.OpenTransaction())
			{
				var table = GetTransactionsIndex(tx, trackedSource);
				foreach (var data in needUpdate.Where(t => !needRemove.Contains(t)))
				{
					await table.Insert(data.Key.ToString(), data.ToBytes());
				}
				foreach (var data in needRemove)
				{
					await table.RemoveKey(data.Key.ToString());
				}
				await tx.Commit();
			}
		}

		TrackedTransaction ToTrackedTransaction(ITrackedTransactionSerializable tx, TrackedSource trackedSource)
		{
			var trackedTransaction = CreateTrackedTransaction(trackedSource, tx);
			trackedTransaction.Inserted = tx.TickCount == 0 ? NBitcoin.Utils.UnixTimeToDateTime(0) : new DateTimeOffset((long)tx.TickCount, TimeSpan.Zero);
			trackedTransaction.FirstSeen = tx.FirstSeenTickCount == 0 ? NBitcoin.Utils.UnixTimeToDateTime(0) : new DateTimeOffset((long)tx.FirstSeenTickCount, TimeSpan.Zero);
			return trackedTransaction;
		}

		public async Task SaveMetadata<TMetadata>(TrackedSource source, string key, TMetadata value) where TMetadata : class
		{
			using var tx = await engine.OpenTransaction();
			var table = GetMetadataIndex(tx, source);
			if (value != null)
			{
				await table.Insert(key, Zip(Serializer.ToString(value)));
				_NoMetadataCache.Remove((source, key));
			}
			else
			{
				await table.RemoveKey(key);
				_NoMetadataCache.Add((source, key));
			}
			await tx.Commit();
		}

		FixedSizeCache<(TrackedSource, String), string> _NoMetadataCache = new FixedSizeCache<(TrackedSource, String), string>(100, (kv) => $"{kv.Item1}:{kv.Item2}");
		public async Task<TMetadata> GetMetadata<TMetadata>(TrackedSource source, string key) where TMetadata : class
		{
			if (_NoMetadataCache.Contains((source, key)))
				return default;
			using var tx = await engine.OpenTransaction();
			var table = GetMetadataIndex(tx, source);
			await foreach (var row in table.SelectForwardSkip(0, key))
			{
				using (row)
				{
					return Serializer.ToObject<TMetadata>(Unzip(await row.ReadValue()));
				}
			}
			_NoMetadataCache.Add((source, key));
			return null;
		}

		public async Task SaveMatches(TrackedTransaction[] transactions)
		{
			if (transactions.Length == 0)
				return;
			var groups = transactions.GroupBy(i => i.TrackedSource);

			using var tx = await engine.OpenTransaction();
			foreach (var group in groups)
			{
				var table = GetTransactionsIndex(tx, group.Key);

				foreach (var value in group)
				{
					if (group.Key is DerivationSchemeTrackedSource s)
					{
						foreach (var kv in value.KnownKeyPathMapping)
						{
							var derivation = s.DerivationStrategy.GetDerivation(kv.Value);
							var info = new KeyPathInformation(derivation, s, keyPathTemplates.GetDerivationFeature(kv.Value), kv.Value, _Network);
							var availableIndex = GetAvailableKeysIndex(tx, s.DerivationStrategy, info.Feature);
							var reservedIndex = GetReservedKeysIndex(tx, s.DerivationStrategy, info.Feature);
							var index = info.GetIndex();
							var bytes = await availableIndex.SelectBytes(index);
							if (bytes != null)
							{
								await availableIndex.RemoveKey(bytes.Key);
							}
							bytes = await reservedIndex.SelectBytes(index);
							if (bytes != null)
							{
								bytes.Dispose();
								await reservedIndex.RemoveKey(bytes.Key);
							}
						}
					}
					var ms = new MemoryStream();
					BitcoinStream bs = new BitcoinStream(ms, true);
					bs.ConsensusFactory = Network.NBitcoinNetwork.Consensus.ConsensusFactory;
					var data = value.CreateBitcoinSerializable();
					bs.ReadWrite(data);
					await table.Insert(data.Key.ToString(), ms.ToArrayEfficient(), false);
				}
			}
			await tx.Commit();
		}

		internal async Task Prune(TrackedSource trackedSource, List<TrackedTransaction> prunable)
		{
			if (prunable == null || prunable.Count == 0)
				return;
			using var tx = await engine.OpenTransaction();
			var table = GetTransactionsIndex(tx, trackedSource);
			foreach (var tracked in prunable)
			{
				await table.RemoveKey(tracked.Key.ToString());
			}
			await tx.Commit();
		}

		internal async Task UpdateAddressPool(DerivationSchemeTrackedSource trackedSource, Dictionary<DerivationFeature, int?> highestKeyIndexFound)
		{
			using var tx = await engine.OpenTransaction();
			foreach (var kv in highestKeyIndexFound)
			{
				if (kv.Value == null)
					continue;
				var index = GetAvailableKeysIndex(tx, trackedSource.DerivationStrategy, kv.Key);
				bool needRefill = await CleanUsed(kv.Value.Value, index);
				index = GetReservedKeysIndex(tx, trackedSource.DerivationStrategy, kv.Key);
				needRefill |= await CleanUsed(kv.Value.Value, index);
				if (needRefill)
				{
					var hIndex = GetHighestPathIndex(tx, trackedSource.DerivationStrategy, kv.Key);
					int highestGenerated = (await hIndex.SelectInt(0)) ?? -1;
					if (highestGenerated < kv.Value.Value)
						await hIndex.Insert(0, kv.Value.Value);
					var toGenerate = await GetAddressToGenerateCount(tx, trackedSource.DerivationStrategy, kv.Key);
					await RefillAvailable(tx, trackedSource.DerivationStrategy, kv.Key, toGenerate);
				}
			}
			await tx.Commit();
		}

		private async Task<bool> CleanUsed(int highestIndex, Index index)
		{
			bool needRefill = false;
			List<string> toRemove = new List<string>();
			foreach (var row in await index.SelectForwardSkip(0).ToArrayAsync())
			{
				using (row)
				{
					var keyInfo = ToObject<KeyPathInformation>(await row.ReadValue());
					if (keyInfo.GetIndex() <= highestIndex)
					{
						await index.RemoveKey(row.Key);
						needRefill = true;
					}
				}
			}
			return needRefill;
		}

		FixedSizeCache<uint256, uint256> noMatchCache = new FixedSizeCache<uint256, uint256>(5000, k => k);
		public Task<TrackedTransaction[]> GetMatches(NBitcoin.Transaction tx, uint256 blockId, DateTimeOffset now, bool useCache)
		{
			return GetMatches(new[] { tx }, blockId, now, useCache);
		}

		private IList<(OutPoint outPoint, Script script)> ExtractInfo(NBitcoin.Transaction txn)
		{
			var outPointWithScript = new List<(OutPoint, Script)>();
			if (!txn.IsCoinBase)
			{
				foreach (var input in txn.Inputs)
				{
					outPointWithScript.Add((input.PrevOut, null));
				}
			}
			foreach (var coin in txn.Outputs.AsCoins())
			{
				if (MinUtxoValue != null && coin.Amount < MinUtxoValue)
					continue;
				outPointWithScript.Add((coin.Outpoint, coin.ScriptPubKey));
			}
			return outPointWithScript;
		}

		private void recordMatches(
									IReadOnlyCollection<NBitcoin.Transaction> txs,
									IReadOnlyCollection<KeyPathInformation> keyPathInformations,
									ref MultiValueDictionary<TrackedTransaction, KeyPathInformation> keyPathInformationsByTrackedTransaction,
									ref HashSet<uint256> noMatchTransactions,
									ref Dictionary<string, TrackedTransaction> matches,
									uint256 blockId,
									DateTimeOffset now
									)
		{
			foreach (var tx in txs)
			{
				if (keyPathInformations.Count != 0)
					noMatchTransactions.Remove(tx.GetHash());
				foreach (var keyInfo in keyPathInformations)
				{
					var matchesGroupingKey = $"{keyInfo.DerivationStrategy?.ToString() ?? keyInfo.ScriptPubKey.ToHex()}-[{tx.GetHash()}]";
					if (!matches.TryGetValue(matchesGroupingKey, out TrackedTransaction match))
					{
						match = CreateTrackedTransaction(keyInfo.TrackedSource,
							new TrackedTransactionKey(tx.GetHash(), blockId, false),
							tx,
							new Dictionary<Script, KeyPath>());
						match.FirstSeen = now;
						match.Inserted = now;
						matches.Add(matchesGroupingKey, match);
					}
					if (keyInfo.KeyPath != null)
						match.KnownKeyPathMapping.TryAdd(keyInfo.ScriptPubKey, keyInfo.KeyPath);
					keyPathInformationsByTrackedTransaction.Add(match, keyInfo);
				}
			}
		}
		public async Task<TrackedTransaction[]> GetMatches(IList<NBitcoin.Transaction> txs, uint256 blockId, DateTimeOffset now, bool useCache)
		{
			foreach (var tx in txs)
				tx.PrecomputeHash(false, true);
			var transactionsPerScript = new MultiValueDictionary<Script, NBitcoin.Transaction>();
			var transactionsPerOutpoint = new MultiValueDictionary<OutPoint, NBitcoin.Transaction>();
			var matches = new Dictionary<string, TrackedTransaction>();
			var noMatchTransactions = new HashSet<uint256>(txs.Count);
			var outPointsWithScripts = new HashSet<(OutPoint outPoint, Script script)>();
			foreach (var tx in txs)
			{
				if (blockId != null && useCache && noMatchCache.Contains(tx.GetHash()))
				{
					continue;
				}
				noMatchTransactions.Add(tx.GetHash());
				var txOutPointsWithScripts = ExtractInfo(tx);

				foreach (var outPointWithScript in txOutPointsWithScripts)
				{
					transactionsPerOutpoint.Add(outPointWithScript.outPoint, tx);
				}
				outPointsWithScripts.AddRange(txOutPointsWithScripts);
			}

			var keyPathInformationsByTrackedTransaction = new MultiValueDictionary<TrackedTransaction, KeyPathInformation>();

			if (outPointsWithScripts.Count > 0)
			{
				var keyInformations = await GetKeyInformations(outPointsWithScripts.ToArray());
				foreach (var keyInfoByOutpoints in keyInformations)
				{
					recordMatches(
						transactionsPerOutpoint[keyInfoByOutpoints.Key],
						keyInfoByOutpoints.Value,
						ref keyPathInformationsByTrackedTransaction,
						ref noMatchTransactions,
						ref matches,
						blockId,
						now
					);
				}
			}

			foreach (var m in matches.Values)
			{
				m.KnownKeyPathMappingUpdated();
				await AfterMatch(m, keyPathInformationsByTrackedTransaction[m]);
			}

			foreach (var tx in txs)
			{
				if (blockId == null &&
					noMatchTransactions.Contains(tx.GetHash()))
				{
					noMatchCache.Add(tx.GetHash());
				}
			}
			return matches.Values.Count == 0 ? Array.Empty<TrackedTransaction>() : matches.Values.ToArray();
		}
		public virtual TrackedTransaction CreateTrackedTransaction(TrackedSource trackedSource, TrackedTransactionKey transactionKey, NBitcoin.Transaction tx, Dictionary<Script, KeyPath> knownScriptMapping)
		{
			return new TrackedTransaction(transactionKey, trackedSource, tx, knownScriptMapping);
		}
		public virtual TrackedTransaction CreateTrackedTransaction(TrackedSource trackedSource, TrackedTransactionKey transactionKey, IEnumerable<Coin> coins, Dictionary<Script, KeyPath> knownScriptMapping)
		{
			return new TrackedTransaction(transactionKey, trackedSource, coins, knownScriptMapping);
		}
		public virtual TrackedTransaction CreateTrackedTransaction(TrackedSource trackedSource, ITrackedTransactionSerializable tx)
		{
			return tx.Key.IsPruned
						? CreateTrackedTransaction(trackedSource, tx.Key, tx.GetCoins(), tx.KnownKeyPathMapping)
						: CreateTrackedTransaction(trackedSource, tx.Key, tx.Transaction, tx.KnownKeyPathMapping);
		}
		protected virtual ITrackedTransactionSerializable CreateBitcoinSerializableTrackedTransaction(TrackedTransactionKey trackedTransactionKey)
		{
			return new TrackedTransaction.TransactionMatchData(trackedTransactionKey);
		}
		protected virtual async Task AfterMatch(TrackedTransaction tx, IReadOnlyCollection<KeyPathInformation> keyInfos)
		{
			var shouldImportRPC = (await GetMetadata<string>(tx.TrackedSource, WellknownMetadataKeys.ImportAddressToRPC)).AsBoolean();
			if (!shouldImportRPC)
				return;
			var accountKey = await GetMetadata<BitcoinExtKey>(tx.TrackedSource, WellknownMetadataKeys.AccountHDKey);
			foreach (var keyInfo in keyInfos)
			{
				await ImportAddressToRPC(accountKey,
					keyInfo.Address,
					keyInfo.KeyPath);
			}
		}

		private async Task ImportAddressToRPC(TrackedSource trackedSource, BitcoinAddress address, KeyPath keyPath)
		{
			var shouldImportRPC = (await GetMetadata<string>(trackedSource, WellknownMetadataKeys.ImportAddressToRPC)).AsBoolean();
			if (!shouldImportRPC)
				return;
			var accountKey = await GetMetadata<BitcoinExtKey>(trackedSource, WellknownMetadataKeys.AccountHDKey);
			await ImportAddressToRPC(accountKey, address, keyPath);
		}
		private async Task ImportAddressToRPC(BitcoinExtKey accountKey, BitcoinAddress address, KeyPath keyPath)
		{
			if (accountKey != null)
			{
				await rpc.ImportPrivKeyAsync(accountKey.Derive(keyPath).PrivateKey.GetWif(Network.NBitcoinNetwork), null, false);
			}
			else
			{
				try
				{
					await rpc.ImportAddressAsync(address, null, false);
				}
				catch (RPCException) // Probably the private key has already been imported
				{

				}
			}
		}

		public async ValueTask<int> DefragmentTables(CancellationToken cancellationToken = default)
		{
			using var tx = await engine.OpenTransaction(cancellationToken);
			var table = tx.GetTable($"{_Suffix}Transactions");
			return await Defragment(tx, table, cancellationToken);
		}

		public async ValueTask<int> TrimmingEvents(int maxEvents, CancellationToken cancellationToken = default)
		{
			using var tx = await engine.OpenTransaction();
			var idx = GetEventsIndex(tx);
			// The first row is not a real event
			var eventCount = await idx.table.GetRecordCount() - 1;
			int deletedEvents = 0;
			while (eventCount > maxEvents)
			{
				var removing = (int)Math.Min(5000, eventCount - maxEvents);
				Logs.Explorer.LogInformation($"{Network.CryptoCode}: There is currently {eventCount} in the event table, removing a batch of {removing} of the oldest one.");
				var rows = await idx.SelectFrom(0, removing).ToArrayAsync();
				foreach (var row in rows)
				{
					// Low evictable page, we should commit
					if (idx.table.PagePool.FreePageCount == 0 &&
						(idx.table.PagePool.EvictablePageCount < idx.table.PagePool.MaxPageCount / 10))
						await tx.Commit();
					await idx.table.Delete(row.Key);
					deletedEvents++;
					row.Dispose();
				}
				await tx.Commit();
				eventCount = await idx.table.GetRecordCount() - 1;
			}
			await Defragment(tx, idx.table, cancellationToken);
			return deletedEvents;
		}

		private async ValueTask<int> Defragment(DBTrie.Transaction tx, Table table, CancellationToken cancellationToken)
		{
			int saved = 0;
			try
			{
				saved = await table.Defragment(cancellationToken);
			}
			catch when (!cancellationToken.IsCancellationRequested)
			{
				Logs.Explorer.LogWarning($"{Network.CryptoCode}: Careful, you are probably running low on storage space, so we attempt defragmentation directly on the table, please do not close NBXplorer during the defragmentation.");
				saved = await table.UnsafeDefragment(cancellationToken);
			}
			await tx.Commit();
			table.ClearCache();
			return saved;
		}

		public async ValueTask<int> MigrateSavedTransactions(CancellationToken cancellationToken = default)
		{
			using var tx = await engine.OpenTransaction(cancellationToken);
			var savedTransactions = GetSavedTransactionTable(tx);
			var legacyTableName = $"{_Suffix}tx-";
			var legacyTables = await tx.Schema.GetTables(legacyTableName).ToArrayAsync();
			if (legacyTables.Length == 0)
				return 0;
			int deleted = 0;
			var lastLogTime = DateTimeOffset.UtcNow;
			foreach (var tableName in legacyTables)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var legacyTable = tx.GetTable(tableName);
				await foreach (var row in legacyTable.Enumerate())
				{
					using (row)
					{
						var txId = tableName.Substring(legacyTableName.Length);
						var blockId = Encoding.UTF8.GetString(row.Key.Span);
						await savedTransactions.Insert(GetSavedTransactionKey(new uint256(txId), blockId == "0" ? null : new uint256(blockId)), await row.ReadValue());
					}
				}
				await tx.Commit();
				await legacyTable.Delete();
				deleted++;
				if (DateTimeOffset.UtcNow - lastLogTime > TimeSpan.FromMinutes(1.0))
				{
					Logs.Explorer.LogInformation($"{Network.CryptoCode}: Still migrating tables {legacyTables.Length - deleted} remaining...");
					lastLogTime = DateTimeOffset.UtcNow;
				}
			}
			return legacyTables.Length;
		}

		private ReadOnlyMemory<byte> GetSavedTransactionKey(uint256 txId, uint256 blockId)
		{
			var key = new byte[blockId is null ? 32 : 64];
			txId.ToBytes(key);
			if (blockId is uint256)
			{
				blockId.ToBytes(key.AsSpan().Slice(32));
			}
			return key.AsMemory();
		}

		private Table GetSavedTransactionTable(DBTrie.Transaction tx)
		{
			return tx.GetTable($"{_Suffix}Txs");
		}
	}
}
