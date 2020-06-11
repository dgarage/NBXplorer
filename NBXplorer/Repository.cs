using DBriize;
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
	public class RepositoryProvider
	{
		DBriizeEngine _Engine;
		Dictionary<string, Repository> _Repositories = new Dictionary<string, Repository>();
		private readonly KeyPathTemplates keyPathTemplates;
		ExplorerConfiguration _Configuration;
		public RepositoryProvider(NBXplorerNetworkProvider networks, KeyPathTemplates keyPathTemplates, ExplorerConfiguration configuration)
		{
			this.keyPathTemplates = keyPathTemplates;
			_Configuration = configuration;
			var directory = Path.Combine(configuration.DataDir, "db");
			if (!Directory.Exists(directory))
				Directory.CreateDirectory(directory);

			int tried = 0;
		retry:
			try
			{
				_Engine = new DBriizeEngine(new DBriizeConfiguration()
				{
					DBriizeDataFolderName = directory
				});
			}
			catch when (tried < 10)
			{
				tried++;
				Thread.Sleep(tried * 500);
				goto retry;
			}
			foreach (var net in networks.GetAll())
			{
				var settings = GetChainSetting(net);
				if (settings != null)
				{
					var repo = net.NBitcoinNetwork.NetworkSet == Liquid.Instance ? new LiquidRepository(_Engine, net, keyPathTemplates, settings.RPC) : new Repository(_Engine, net, keyPathTemplates, settings.RPC);
					repo.MaxPoolSize = configuration.MaxGapSize;
					repo.MinPoolSize = configuration.MinGapSize;
					repo.MinUtxoValue = settings.MinUtxoValue;
					_Repositories.Add(net.CryptoCode, repo);
				}
			}
		}

		private ChainConfiguration GetChainSetting(NBXplorerNetwork net)
		{
			return _Configuration.ChainConfigurations.FirstOrDefault(c => c.CryptoCode == net.CryptoCode);
		}

		public async Task StartAsync()
		{
			await Task.WhenAll(_Repositories.Select(kv => kv.Value.StartAsync()).ToArray());
			foreach (var repo in _Repositories.Select(kv => kv.Value))
			{
				if (GetChainSetting(repo.Network) is ChainConfiguration chainConf && chainConf.Rescan)
				{
					Logs.Configuration.LogInformation($"{repo.Network.CryptoCode}: Rescanning the chain...");
					await repo.SetIndexProgress(null);
				}
			}
		}

		public IEnumerable<Repository> GetAll()
		{
			return _Repositories.Values;
		}

		public Repository GetRepository(NBXplorerNetwork network)
		{
			_Repositories.TryGetValue(network.CryptoCode, out Repository repository);
			return repository;
		}

		public async Task DisposeAsync()
		{
			await Task.WhenAll(_Repositories.Select(kv => kv.Value.DisposeAsync()).ToArray());
			_Engine.Dispose();
		}
	}

	public class Repository
	{


		public async Task Ping()
		{
			await _TxContext.DoAsync((tx) =>
			{
			});
		}

		public async Task CancelReservation(DerivationStrategyBase strategy, KeyPath[] keyPaths)
		{
			if (keyPaths.Length == 0)
				return;
			await _TxContext.DoAsync(tx =>
			{
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
							var data = reserved.SelectBytes(key);
							if (data == null)
								continue;
							reserved.RemoveKey(key);
							available.Insert(key, data);
							needCommit = true;
						}
					}
				}
				if (needCommit)
					tx.Commit();
			});
		}

		class Index
		{
			DBriize.Transactions.Transaction tx;
			public Index(DBriize.Transactions.Transaction tx, string tableName, string primaryKey)
			{
				TableName = tableName;
				PrimaryKey = primaryKey;
				this.tx = tx;
			}


			public string TableName
			{
				get; set;
			}
			public string PrimaryKey
			{
				get; set;
			}

			public byte[] SelectBytes(int index)
			{
				var bytes = tx.Select<string, byte[]>(TableName, $"{PrimaryKey}-{index:D10}");
				if (bytes == null || !bytes.Exists)
					return null;
				return bytes.Value;
			}

			public void RemoveKey(string index)
			{
				tx.RemoveKey(TableName, $"{PrimaryKey}-{index}");
			}

			public void Insert(string key, byte[] value)
			{
				tx.Insert(TableName, $"{PrimaryKey}-{key}", value);
			}

			public (string Key, byte[] Value)[] SelectForwardSkip(int n, string startWith = null)
			{
				if (startWith == null)
					startWith = PrimaryKey;
				else
					startWith = $"{PrimaryKey}-{startWith}";
				return tx.SelectForwardStartsWith<string, byte[]>(TableName, startWith).Skip(n).Select(c => (c.Key, c.Value)).ToArray();
			}

			public (long Key, byte[] Value)[] SelectFrom(long key, int? limit)
			{
				return tx.SelectForwardStartFrom<string, byte[]>(TableName, $"{PrimaryKey}-{key:D20}", false)
						.Take(limit == null ? Int32.MaxValue : limit.Value)
						.Where(r => r.Exists && r.Value != null)
						.Select(r => (ExtractLong(r.Key), r.Value))
						.ToArray();
			}

			private long ExtractLong(string key)
			{
				var span = key.AsSpan();
				var sep = span.LastIndexOf('-');
				span = span.Slice(sep + 1);
				return long.Parse(span);
			}

			public int Count()
			{
				return tx.SelectForwardStartsWith<string, byte[]>(TableName, PrimaryKey).Count();
			}

			public void Insert(int key, byte[] value)
			{
				Insert($"{key:D10}", value);
			}
			public void Insert(long key, byte[] value)
			{
				Insert($"{key:D20}", value);
			}
			public void RemoveKey(int index)
			{
				RemoveKey($"{index:D10}");
			}
			public int? SelectInt(int index)
			{
				var bytes = SelectBytes(index);
				if (bytes == null)
					return null;
				bytes[0] = (byte)(bytes[0] & ~0x80);
				return (int)NBitcoin.Utils.ToUInt32(bytes, false);
			}
			public long? SelectLong(int index)
			{
				var bytes = SelectBytes(index);
				if (bytes == null)
					return null;
				bytes[0] = (byte)(bytes[0] & ~0x80);
				return (int)NBitcoin.Utils.ToUInt64(bytes, false);
			}
			public void Insert(int key, int value)
			{
				var bytes = NBitcoin.Utils.ToBytes((uint)value, false);
				Insert(key, bytes);
			}
			public void Insert(int key, long value)
			{
				var bytes = NBitcoin.Utils.ToBytes((ulong)value, false);
				Insert(key, bytes);
			}
		}

		Index GetAvailableKeysIndex(DBriize.Transactions.Transaction tx, DerivationStrategyBase trackedSource, DerivationFeature feature)
		{
			return new Index(tx, $"{_Suffix}AvailableKeys", $"{trackedSource.GetHash()}-{feature}");
		}

		Index GetScriptsIndex(DBriize.Transactions.Transaction tx, Script scriptPubKey)
		{
			return new Index(tx, $"{_Suffix}Scripts", $"{scriptPubKey.Hash}");
		}

		Index GetHighestPathIndex(DBriize.Transactions.Transaction tx, DerivationStrategyBase strategy, DerivationFeature feature)
		{
			return new Index(tx, $"{_Suffix}HighestPath", $"{strategy.GetHash()}-{feature}");
		}

		Index GetReservedKeysIndex(DBriize.Transactions.Transaction tx, DerivationStrategyBase strategy, DerivationFeature feature)
		{
			return new Index(tx, $"{_Suffix}ReservedKeys", $"{strategy.GetHash()}-{feature}");
		}

		Index GetTransactionsIndex(DBriize.Transactions.Transaction tx, TrackedSource trackedSource)
		{
			return new Index(tx, $"{_Suffix}Transactions", $"{trackedSource.GetHash()}");
		}

		Index GetMetadataIndex(DBriize.Transactions.Transaction tx, TrackedSource trackedSource)
		{
			return new Index(tx, $"{_Suffix}Metadata", $"{trackedSource.GetHash()}");
		}

		Index GetEventsIndex(DBriize.Transactions.Transaction tx)
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

		DBriizeTransactionContext _TxContext;
		internal Repository(DBriizeEngine engine, NBXplorerNetwork network, KeyPathTemplates keyPathTemplates, RPCClient rpc)
		{
			if (network == null)
				throw new ArgumentNullException(nameof(network));
			_Network = network;
			this.keyPathTemplates = keyPathTemplates;
			this.rpc = rpc;
			Serializer = new Serializer(_Network);
			_Network = network;
			_TxContext = new DBriizeTransactionContext(engine);
			_TxContext.UnhandledException += (s, ex) =>
			{
				Logs.Explorer.LogCritical(ex, $"{network.CryptoCode}: Unhandled exception in the repository");
			};
			_Suffix = network.CryptoCode == "BTC" ? "" : network.CryptoCode;
		}

		public Task StartAsync()
		{
			return _TxContext.StartAsync();
		}
		public Task DisposeAsync()
		{
			return _TxContext.DisposeAsync();
		}

		public string _Suffix;
		public Task<BlockLocator> GetIndexProgress()
		{
			return _TxContext.DoAsync<BlockLocator>(tx =>
			{
				tx.ValuesLazyLoadingIsOn = false;
				var existingRow = tx.Select<string, byte[]>($"{_Suffix}IndexProgress", "");
				if (existingRow == null || !existingRow.Exists)
					return null;
				BlockLocator locator = new BlockLocator();
				locator.FromBytes(existingRow.Value);
				return locator;
			});
		}

		public Task SetIndexProgress(BlockLocator locator)
		{
			return _TxContext.DoAsync(tx =>
			{
				if (locator == null)
					tx.RemoveKey($"{_Suffix}IndexProgress", "");
				else
					tx.Insert($"{_Suffix}IndexProgress", "", locator.ToBytes());
				tx.Commit();
			});
		}

		public async Task<KeyPathInformation> GetUnused(DerivationStrategyBase strategy, DerivationFeature derivationFeature, int n, bool reserve)
		{
			var keyInfo = await _TxContext.DoAsync((tx) =>
			{
				tx.ValuesLazyLoadingIsOn = false;
				var availableTable = GetAvailableKeysIndex(tx, strategy, derivationFeature);
				var reservedTable = GetReservedKeysIndex(tx, strategy, derivationFeature);
				var rows = availableTable.SelectForwardSkip(n);
				if (rows.Length == 0)
					return null;

				var keyInfo = ToObject<KeyPathInformation>(rows[0].Value).AddAddress(Network.NBitcoinNetwork);
				if (reserve)
				{
					availableTable.RemoveKey(keyInfo.GetIndex());
					reservedTable.Insert(keyInfo.GetIndex(), rows[0].Value);
					tx.Commit();
				}
				return keyInfo;
			});
			if (keyInfo != null)
			{
				await ImportAddressToRPC(keyInfo.TrackedSource, keyInfo.Address, keyInfo.KeyPath);
			}
			return keyInfo;
		}

		int GetAddressToGenerateCount(DBriize.Transactions.Transaction tx, DerivationStrategyBase strategy, DerivationFeature derivationFeature)
		{
			var availableTable = GetAvailableKeysIndex(tx, strategy, derivationFeature);
			var currentlyAvailable = availableTable.Count();
			if (currentlyAvailable >= MinPoolSize)
				return 0;
			return Math.Max(0, MaxPoolSize - currentlyAvailable);
		}

		private void RefillAvailable(DBriize.Transactions.Transaction tx, DerivationStrategyBase strategy, DerivationFeature derivationFeature, int toGenerate)
		{
			if (toGenerate <= 0)
				return;
			var availableTable = GetAvailableKeysIndex(tx, strategy, derivationFeature);
			var highestTable = GetHighestPathIndex(tx, strategy, derivationFeature);
			int highestGenerated = highestTable.SelectInt(0) ?? -1;
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
				GetScriptsIndex(tx, info.ScriptPubKey).Insert($"{strategy.GetHash()}-{derivationFeature}", bytes);
				availableTable.Insert(index, bytes);
			}
			highestTable.Insert(0, highestGenerated + toGenerate);
			tx.Commit();
		}

		public Task<long> SaveEvent(NewEventBase evt)
		{
			// Fetch the lastEventId on row 0
			// Increment it,
			// Insert event
			return _TxContext.DoAsync((tx) =>
			{
				var idx = GetEventsIndex(tx);
				var lastEventIndexMaybe = idx.SelectLong(0);
				var lastEventIndex = lastEventIndexMaybe.HasValue ? lastEventIndexMaybe.Value + 1 : 1;
				idx.Insert(0, lastEventIndex);
				idx.Insert(lastEventIndex, this.ToBytes(evt.ToJObject(Serializer.Settings)));
				tx.Commit();
				lastKnownEventIndex = lastEventIndex;
				return lastEventIndex;
			});
		}
		long lastKnownEventIndex = -1;
		public Task<IList<NewEventBase>> GetLatestEvents(int limit = 10)
		{
			return _TxContext.DoAsync((tx) =>
			{
				if (limit < 1)
					return new List<NewEventBase>();
				tx.ValuesLazyLoadingIsOn = false;
				// Find the last event id
				var idx = GetEventsIndex(tx);
				var lastEventIndexMaybe = idx.SelectLong(0);
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
				IList<NewEventBase> evts = new List<NewEventBase>(query.Length);
				foreach (var value in query)
				{
					var evt = NewEventBase.ParseEvent(ToObject<JObject>(value.Value), Serializer.Settings);
					evt.EventId = value.Key;
					evts.Add(evt);
				}
				return evts;
			});
		}
		public Task<IList<NewEventBase>> GetEvents(long lastEventId, int? limit = null)
		{
			if (lastEventId < 1 && limit.HasValue && limit.Value != int.MaxValue)
				limit = limit.Value + 1; // The row with key 0 holds the lastEventId
			return _TxContext.DoAsync((tx) =>
			{
				if (lastKnownEventIndex != -1 && lastKnownEventIndex == lastEventId)
					return new List<NewEventBase>();
				tx.ValuesLazyLoadingIsOn = false;
				var idx = GetEventsIndex(tx);
				var query = idx.SelectFrom(lastEventId, limit);
				IList<NewEventBase> evts = new List<NewEventBase>(query.Length);
				foreach (var value in query)
				{
					if (value.Key == 0) // Last Index
						continue;
					var evt = NewEventBase.ParseEvent(ToObject<JObject>(value.Value), Serializer.Settings);
					evt.EventId = value.Key;
					evts.Add(evt);
				}
				return evts;
			});
		}

		public Task SaveKeyInformations(KeyPathInformation[] keyPathInformations)
		{
			return _TxContext.DoAsync((tx) =>
			{
				foreach (var info in keyPathInformations)
				{
					var bytes = ToBytes(info);
					GetScriptsIndex(tx, info.ScriptPubKey).Insert($"{info.DerivationStrategy.GetHash()}-{info.Feature}", bytes);
				}
				tx.Commit();
			});
		}

		public Task Track(IDestination address)
		{
			return _TxContext.DoAsync((tx) =>
			{
				var info = new KeyPathInformation()
				{
					ScriptPubKey = address.ScriptPubKey,
					TrackedSource = (TrackedSource)address,
					Address = (address as BitcoinAddress) ?? address.ScriptPubKey.GetDestinationAddress(Network.NBitcoinNetwork)
				};
				var bytes = ToBytes(info);
				GetScriptsIndex(tx, address.ScriptPubKey).Insert(address.ScriptPubKey.Hash.ToString(), bytes);
				tx.Commit();
			});
		}

		public Task<int> GenerateAddresses(DerivationStrategyBase strategy, DerivationFeature derivationFeature, int maxAddresses)
		{
			return GenerateAddresses(strategy, derivationFeature, new GenerateAddressQuery(null, maxAddresses));
		}
		public Task<int> GenerateAddresses(DerivationStrategyBase strategy, DerivationFeature derivationFeature, GenerateAddressQuery query = null)
		{
			query = query ?? new GenerateAddressQuery();
			return _TxContext.DoAsync((tx) =>
			{
				var toGenerate = GetAddressToGenerateCount(tx, strategy, derivationFeature);
				if (query.MaxAddresses is int max)
					toGenerate = Math.Min(max, toGenerate);
				if (query.MinAddresses is int min)
					toGenerate = Math.Max(min, toGenerate);
				RefillAvailable(tx, strategy, derivationFeature, toGenerate);
				return toGenerate;
			});
		}

		class TimeStampedTransaction : IBitcoinSerializable
		{

			public TimeStampedTransaction()
			{

			}
			public TimeStampedTransaction(Network network, byte[] hex)
			{
				var stream = new BitcoinStream(hex);
				stream.ConsensusFactory = network.Consensus.ConsensusFactory;
				this.ReadWrite(stream);
			}

			public TimeStampedTransaction(Transaction tx, ulong timestamp)
			{
				_TimeStamp = timestamp;
				_Transaction = tx;
			}
			Transaction _Transaction;
			public Transaction Transaction
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
			if (transactions.Length == 0)
				return result;
			foreach (var batch in transactions.Batch(BatchSize))
			{
				await _TxContext.DoAsync(tx =>
				{
					var date = NBitcoin.Utils.DateTimeToUnixTime(now);
					foreach (var btx in batch)
					{
						var timestamped = new TimeStampedTransaction(btx, date);
						var key = blockHash == null ? "0" : blockHash.ToString();
						var value = timestamped.ToBytes();
						tx.Insert($"{_Suffix}tx-" + btx.GetHash().ToString(), key, value);
						result.Add(ToSavedTransaction(Network.NBitcoinNetwork, key, value));
					}
					tx.Commit();
				});
			}
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
			await _TxContext.DoAsync(tx =>
			{
				foreach (var row in tx.SelectForward<string, byte[]>($"{_Suffix}tx-" + txid.ToString()))
				{
					SavedTransaction t = ToSavedTransaction(Network.NBitcoinNetwork, row.Key, row.Value);
					saved.Add(t);
				}
			});
			return saved.ToArray();
		}

		private static SavedTransaction ToSavedTransaction(Network network, string key, byte[] value)
		{
			SavedTransaction t = new SavedTransaction();
			if (key.Length != 1)
				t.BlockHash = new uint256(key);
			var timeStamped = new TimeStampedTransaction(network, value);
			t.Transaction = timeStamped.Transaction;
			t.Timestamp = NBitcoin.Utils.UnixTimeToDateTime(timeStamped.TimeStamp);
			t.Transaction.PrecomputeHash(true, false);
			return t;
		}
		public async Task<MultiValueDictionary<Script, KeyPathInformation>> GetKeyInformations(Script[] scripts)
		{
			MultiValueDictionary<Script, KeyPathInformation> result = new MultiValueDictionary<Script, KeyPathInformation>();
			if (scripts.Length == 0)
				return result;
			foreach (var batch in scripts.Batch(BatchSize))
			{
				await _TxContext.DoAsync(tx =>
				{
					tx.ValuesLazyLoadingIsOn = false;
					foreach (var script in batch)
					{
						var table = GetScriptsIndex(tx, script);
						var keyInfos = table.SelectForwardSkip(0)
											.Select(r => ToObject<KeyPathInformation>(r.Value).AddAddress(Network.NBitcoinNetwork))
											// Because xpub are mutable (several xpub map to same script)
											// an attacker could generate lot's of xpub mapping to the same script
											// and this would blow up here. This we take only 5 results max.
											.Take(5)
											.ToArray();
						result.AddRange(script, keyInfos);
					}
				});
			}
			return result;
		}

		public Serializer Serializer
		{
			get; private set;
		}

		private T ToObject<T>(byte[] value)
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
		private string Unzip(byte[] bytes)
		{
			MemoryStream ms = new MemoryStream(bytes);
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
			var transactions = await _TxContext.DoAsync(tx =>
			{
				var table = GetTransactionsIndex(tx, trackedSource);
				tx.ValuesLazyLoadingIsOn = false;
				var result = new List<ITrackedTransactionSerializable>();
				foreach (var row in table.SelectForwardSkip(0, txId?.ToString()))
				{
					MemoryStream ms = new MemoryStream(row.Value);
					BitcoinStream bs = new BitcoinStream(ms, false);
					bs.ConsensusFactory = Network.NBitcoinNetwork.Consensus.ConsensusFactory;
					var data = CreateBitcoinSerializableTrackedTransaction(TrackedTransactionKey.Parse(row.Key));
					data.ReadWrite(bs);
					result.Add(data);
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
				return result;
			}, cancellation);

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
				// This can be eventually consistent, let's not waste one round trip waiting for this
				_TxContext.DoAsync(tx =>
				{
					var table = GetTransactionsIndex(tx, trackedSource);
					foreach (var data in needUpdate.Where(t => !needRemove.Contains(t)))
					{
						table.Insert(data.Key.ToString(), data.ToBytes());
					}
					foreach (var data in needRemove)
					{
						table.RemoveKey(data.Key.ToString());
					}
					tx.Commit();
				});
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
			}
			return transactions.Where(tt => !needRemove.Contains(tt)).Select(c => ToTrackedTransaction(c, trackedSource)).ToArray();
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
			await _TxContext.DoAsync(tx =>
			{
				var table = GetMetadataIndex(tx, source);
				if (value != null)
				{
					table.Insert(key, Zip(Serializer.ToString(value)));
					_NoMetadataCache.Remove((source, key));
				}
				else
				{
					table.RemoveKey(key);
					_NoMetadataCache.Add((source, key));
				}
				tx.Commit();
			});
		}

		FixedSizeCache<(TrackedSource, String), string> _NoMetadataCache = new FixedSizeCache<(TrackedSource, String), string>(100, (kv) => $"{kv.Item1}:{kv.Item2}");
		public async Task<TMetadata> GetMetadata<TMetadata>(TrackedSource source, string key) where TMetadata : class
		{
			if (_NoMetadataCache.Contains((source, key)))
				return default;
			return await _TxContext.DoAsync(tx =>
			{
				var table = GetMetadataIndex(tx, source);
				foreach (var row in table.SelectForwardSkip(0, key))
				{
					return Serializer.ToObject<TMetadata>(Unzip(row.Value));
				}
				_NoMetadataCache.Add((source, key));
				return null;
			});
		}

		public async Task SaveMatches(TrackedTransaction[] transactions)
		{
			if (transactions.Length == 0)
				return;
			var groups = transactions.GroupBy(i => i.TrackedSource);

			await _TxContext.DoAsync(tx =>
			{
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
								var bytes = availableIndex.SelectBytes(index);
								if (bytes != null)
								{
									availableIndex.RemoveKey(index);
								}
								bytes = reservedIndex.SelectBytes(index);
								if (bytes != null)
								{
									reservedIndex.RemoveKey(index);
								}
							}
						}
						var ms = new MemoryStream();
						BitcoinStream bs = new BitcoinStream(ms, true);
						bs.ConsensusFactory = Network.NBitcoinNetwork.Consensus.ConsensusFactory;
						var data = value.CreateBitcoinSerializable();
						bs.ReadWrite(data);
						table.Insert(data.Key.ToString(), ms.ToArrayEfficient());
					}
				}
				tx.Commit();
			});
		}

		internal Task Prune(TrackedSource trackedSource, List<TrackedTransaction> prunable)
		{
			if (prunable == null || prunable.Count == 0)
				return Task.CompletedTask;
			return _TxContext.DoAsync(tx =>
			{
				var table = GetTransactionsIndex(tx, trackedSource);
				foreach (var tracked in prunable)
				{
					table.RemoveKey(tracked.Key.ToString());
				}
				tx.Commit();
			});
		}

		public async Task CleanTransactions(TrackedSource trackedSource, List<TrackedTransaction> cleanList)
		{
			if (cleanList == null || cleanList.Count == 0)
				return;
			await _TxContext.DoAsync(tx =>
			{
				var table = GetTransactionsIndex(tx, trackedSource);
				foreach (var tracked in cleanList)
				{
					table.RemoveKey(tracked.Key.ToString());
				}
				tx.Commit();
			});
		}

		internal Task UpdateAddressPool(DerivationSchemeTrackedSource trackedSource, Dictionary<DerivationFeature, int?> highestKeyIndexFound)
		{
			return _TxContext.DoAsync(tx =>
			{
				tx.ValuesLazyLoadingIsOn = false;
				foreach (var kv in highestKeyIndexFound)
				{
					if (kv.Value == null)
						continue;
					var index = GetAvailableKeysIndex(tx, trackedSource.DerivationStrategy, kv.Key);
					bool needRefill = CleanUsed(kv.Value.Value, index);
					index = GetReservedKeysIndex(tx, trackedSource.DerivationStrategy, kv.Key);
					needRefill |= CleanUsed(kv.Value.Value, index);
					if (needRefill)
					{
						var hIndex = GetHighestPathIndex(tx, trackedSource.DerivationStrategy, kv.Key);
						int highestGenerated = hIndex.SelectInt(0) ?? -1;
						if (highestGenerated < kv.Value.Value)
							hIndex.Insert(0, kv.Value.Value);
						var toGenerate = GetAddressToGenerateCount(tx, trackedSource.DerivationStrategy, kv.Key);
						RefillAvailable(tx, trackedSource.DerivationStrategy, kv.Key, toGenerate);
					}
				}
				tx.Commit();
			});
		}

		private bool CleanUsed(int highestIndex, Index index)
		{
			bool needRefill = false;
			foreach (var row in index.SelectForwardSkip(0))
			{
				var keyInfo = ToObject<KeyPathInformation>(row.Value);
				if (keyInfo.GetIndex() <= highestIndex)
				{
					index.RemoveKey(keyInfo.GetIndex());
					needRefill = true;
				}
			}
			return needRefill;
		}

		FixedSizeCache<uint256, uint256> noMatchCache = new FixedSizeCache<uint256, uint256>(5000, k => k);
		public Task<TrackedTransaction[]> GetMatches(Transaction tx, uint256 blockId, DateTimeOffset now, bool useCache)
		{
			return GetMatches(new[] { tx }, blockId, now, useCache);
		}
		public async Task<TrackedTransaction[]> GetMatches(IList<Transaction> txs, uint256 blockId, DateTimeOffset now, bool useCache)
		{
			foreach (var tx in txs)
				tx.PrecomputeHash(false, true);
			var transactionsPerScript = new MultiValueDictionary<Script, Transaction>();
			var matches = new Dictionary<string, TrackedTransaction>();
			HashSet<Script> scripts = new HashSet<Script>(txs.Count);
			var noMatchTransactions = new HashSet<uint256>(txs.Count);
			foreach (var tx in txs)
			{
				if (blockId != null && useCache && noMatchCache.Contains(tx.GetHash()))
				{
					continue;
				}
				noMatchTransactions.Add(tx.GetHash());
				foreach (var input in tx.Inputs)
				{
					var signer = input.GetSigner();
					if (signer != null)
					{
						scripts.Add(signer.ScriptPubKey);
						transactionsPerScript.Add(signer.ScriptPubKey, tx);
					}
				}
				foreach (var output in tx.Outputs)
				{
					if (MinUtxoValue != null && output.Value < MinUtxoValue)
						continue;
					scripts.Add(output.ScriptPubKey);
					transactionsPerScript.Add(output.ScriptPubKey, tx);
				}
			}
			if (scripts.Count == 0)
				return Array.Empty<TrackedTransaction>();
			var keyPathInformationsByTrackedTransaction = new MultiValueDictionary<TrackedTransaction, KeyPathInformation>();
			var keyInformations = await GetKeyInformations(scripts.ToArray());
			foreach (var keyInfoByScripts in keyInformations)
			{
				foreach (var tx in transactionsPerScript[keyInfoByScripts.Key])
				{
					if (keyInfoByScripts.Value.Count != 0)
						noMatchTransactions.Remove(tx.GetHash());
					foreach (var keyInfo in keyInfoByScripts.Value)
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
		public virtual TrackedTransaction CreateTrackedTransaction(TrackedSource trackedSource, TrackedTransactionKey transactionKey, Transaction tx, Dictionary<Script, KeyPath> knownScriptMapping)
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
	}
}
