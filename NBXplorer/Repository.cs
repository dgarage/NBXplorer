using Microsoft.Extensions.Logging;
using System.Linq;
using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.JsonConverters;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System.Threading.Tasks;
using System.Threading;
using NBitcoin.DataEncoders;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using NBXplorer.Logging;
using NBXplorer.Configuration;
using static NBXplorer.RepositoryProvider;
using static NBXplorer.Repository;
using NBXplorer.DB;

namespace NBXplorer
{
	public class TrackedTransaction
	{
		public uint256 BlockHash
		{
			get; set;
		}

		public Transaction Transaction
		{
			get; set;
		}

		public DateTimeOffset Inserted
		{
			get; set;
		}

		public DateTimeOffset FirstSeen
		{
			get; set;
		}

		public TransactionMiniMatch TransactionMatch
		{
			get; set;
		}
		internal string GetRowKey()
		{
			return $"{Transaction.GetHash()}:{BlockHash}";
		}
	}

	public class MatchedTransaction
	{
		public uint256 BlockId
		{
			get; set;
		}

		public TransactionMatch Match
		{
			get; set;
		}
	}

	public class RepositoryProvider : IDisposable
	{
		Dictionary<string, Repository> _Repositories = new Dictionary<string, Repository>();
		NBXplorerContextFactory _ContextFactory;
		public RepositoryProvider(NBXplorerContextFactory contextFactory, NBXplorerNetworkProvider networks, ExplorerConfiguration configuration)
		{
			var directory = Path.Combine(configuration.DataDir, "db");
			if(!Directory.Exists(directory))
				Directory.CreateDirectory(directory);
			_ContextFactory = contextFactory;
			foreach(var net in networks.GetAll())
			{
				var settings = configuration.ChainConfigurations.FirstOrDefault(c => c.CryptoCode == net.CryptoCode);
				if(settings != null)
				{
					var repo = new Repository(_ContextFactory, net);
					repo.MaxPoolSize = configuration.MaxGapSize;
					repo.MinPoolSize = configuration.MinGapSize;
					_Repositories.Add(net.CryptoCode, repo);
					if(settings.Rescan)
					{
						Logs.Configuration.LogInformation($"Rescanning the {net.CryptoCode} chain...");
						repo.SetIndexProgress(null).GetAwaiter().GetResult();
					}
				}
			}
		}

		public Repository GetRepository(NBXplorerNetwork network)
		{
			_Repositories.TryGetValue(network.CryptoCode, out Repository repository);
			return repository;
		}

		public void Dispose()
		{

		}
	}

	public class Repository
	{


		public void Ping()
		{
		}

		public async Task CancelReservation(DerivationStrategyBase strategy, KeyPath[] keyPaths)
		{
			if(keyPaths.Length == 0)
				return;

			using(var tx = _ContextFactory.CreateContext())
			{
				tx.ForceDelete = true;
				bool needCommit = false;
				var featuresPerKeyPaths = Enum.GetValues(typeof(DerivationFeature)).Cast<DerivationFeature>()
				.Select(f => (Feature: f, Path: DerivationStrategyBase.GetKeyPath(f)))
				.ToDictionary(o => o.Path, o => o.Feature);

				var groups = keyPaths.Where(k => k.Indexes.Length > 0).GroupBy(k => k.Parent);
				foreach(var group in groups)
				{
					if(featuresPerKeyPaths.TryGetValue(group.Key, out DerivationFeature feature))
					{
						var reserved = GetReservedKeysIndex(strategy, feature);
						var available = GetAvailableKeysIndex(strategy, feature);
						foreach(var keyPath in group)
						{
							var key = (int)keyPath.Indexes.Last();
							var data = await reserved.Select<byte[]>(tx, key);
							if(data == null || !data.Exists)
								continue;
							reserved.RemoveKey(tx, key);
							available.Insert(tx, key, data.Value);
							needCommit = true;
						}
					}
				}
				if(needCommit)
					await tx.CommitAsync();
			}
		}

		internal class Index
		{
			public Index(string tableName, string primaryKey)
			{
				TableName = tableName;
				PrimaryKey = primaryKey;
			}


			public string TableName
			{
				get; set;
			}
			public string PrimaryKey
			{
				get; set;
			}

			public Task<GenericRow<T>> Select<T>(NBXplorerDBContext tx, int index)
			{
				return tx.Select<T>(TableName, $"{PrimaryKey}-{index:D10}");
			}

			public void RemoveKey(NBXplorerDBContext tx, int index)
			{
				tx.RemoveKey(TableName, $"{PrimaryKey}-{index:D10}");
			}

			public void RemoveKey(NBXplorerDBContext tx, string index)
			{
				tx.RemoveKey(TableName, $"{PrimaryKey}-{index}");
			}

			public async Task<IEnumerable<GenericRow<T>>> SelectForwardSkip<T>(NBXplorerDBContext tx, int n)
			{
				return (await tx.SelectForwardStartsWith<T>(TableName, PrimaryKey)).Skip(n);
			}

			public void Insert<T>(NBXplorerDBContext tx, int key, T value)
			{
				tx.Insert(TableName, $"{PrimaryKey}-{key:D10}", value);
			}
			public void Insert<T>(NBXplorerDBContext tx, string key, T value)
			{
				tx.Insert(TableName, $"{PrimaryKey}-{key}", value);
			}

			public int Count(NBXplorerDBContext tx)
			{
				return tx.Count(TableName, PrimaryKey);
			}

			internal Task<bool> ReleaseLock(NBXplorerDBContext tx)
			{
				return tx.ReleaseLock(TableName, PrimaryKey);
			}

			internal Task<bool> TakeLock(NBXplorerDBContext tx)
			{
				return tx.TakeLock(TableName, PrimaryKey);
			}
		}

		Index GetAvailableKeysIndex(DerivationStrategyBase strategy, DerivationFeature feature)
		{
			return new Index($"{_Suffix}AvailableKeys", $"{strategy.GetHash()}-{feature}");
		}

		Index GetScriptsIndex(Script scriptPubKey)
		{
			return new Index($"{_Suffix}Scripts", $"{scriptPubKey.Hash}");
		}

		Index GetHighestPathIndex(DerivationStrategyBase strategy, DerivationFeature feature)
		{
			return new Index($"{_Suffix}HighestPath", $"{strategy.GetHash()}-{feature}");
		}

		Index GetReservedKeysIndex(DerivationStrategyBase strategy, DerivationFeature feature)
		{
			return new Index($"{_Suffix}ReservedKeys", $"{strategy.GetHash()}-{feature}");
		}

		Index GetTransactionsIndex(DerivationStrategyBase derivation)
		{
			return new Index($"{_Suffix}Transactions", $"{derivation.GetHash()}");
		}

		NBXplorerNetwork _Network;

		public NBXplorerNetwork Network
		{
			get
			{
				return _Network;
			}
		}

		NBXplorerContextFactory _ContextFactory;
		internal Repository(NBXplorerContextFactory contextFactory, NBXplorerNetwork network)
		{
			if(network == null)
				throw new ArgumentNullException(nameof(network));
			_Network = network;
			Serializer = new Serializer(_Network.NBitcoinNetwork);
			_Network = network;
			_ContextFactory = contextFactory;
			_Suffix = network.CryptoCode;
		}
		string _Suffix;
		public async Task<BlockLocator> GetIndexProgress()
		{
			using(var tx = _ContextFactory.CreateContext())
			{
				tx.ValuesLazyLoadingIsOn = false;
				var existingRow = await tx.Select<byte[]>($"{_Suffix}IndexProgress", "");
				if(existingRow == null || !existingRow.Exists)
					return null;
				BlockLocator locator = new BlockLocator();
				locator.FromBytes(existingRow.Value);
				return locator;
			}
		}

		public async Task SetIndexProgress(BlockLocator locator)
		{
			using(var tx = _ContextFactory.CreateContext())
			{
				if(locator == null)
					tx.RemoveKey($"{_Suffix}IndexProgress", "");
				else
					tx.Insert($"{_Suffix}IndexProgress", "", locator.ToBytes());
				await tx.CommitAsync();
			}
		}

		public class DBLock
		{
			private Repository repository;
			private Index index;

			internal DBLock(Repository repository, Index index)
			{
				this.repository = repository;
				this.index = index;
			}

			public async Task<bool> ReleaseLock()
			{
				using(var tx = repository._ContextFactory.CreateContext())
				{
					return await index.ReleaseLock(tx);
				}
			}
		}

		public async Task<DBLock> TakeWalletLock(DerivationStrategyBase strategy, CancellationToken cancellation = default(CancellationToken))
		{
			using(var tx = _ContextFactory.CreateContext())
			{
				var index = new Index($"{_Suffix}Locks", $"{strategy.GetHash()}");
				while(!await index.TakeLock(tx))
				{
					await Task.Delay(500, cancellation);
				}
				return new DBLock(this, index);
			}
		}

		public async Task<KeyPathInformation> GetUnused(DerivationStrategyBase strategy, DerivationFeature derivationFeature, int n, bool reserve)
		{
			var delay = TimeSpan.FromMilliseconds(50 + NBitcoin.RandomUtils.GetUInt32() % 100);
			using(var tx = _ContextFactory.CreateContext())
			using(var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
			{
				tx.ValuesLazyLoadingIsOn = false;
				while(!cts.IsCancellationRequested)
				{
					var availableTable = GetAvailableKeysIndex(strategy, derivationFeature);
					var reservedTable = GetReservedKeysIndex(strategy, derivationFeature);
					var bytes = (await availableTable.SelectForwardSkip<byte[]>(tx, n)).FirstOrDefault()?.Value;
					if(bytes == null)
					{
						await Task.Delay(delay);
						continue;
					}
					var keyInfo = ToObject<KeyPathInformation>(bytes);
					keyInfo.Address = keyInfo.ScriptPubKey.GetDestinationAddress(Network.NBitcoinNetwork).ToString();
					if(reserve)
					{
						await tx.CommitAsync();
						availableTable.RemoveKey(tx, (int)keyInfo.KeyPath.Indexes.Last());
						if(await tx.CommitAsync() == 0)
						{
							// We are not the one who took the available address, let's try again
							await Task.Delay(delay);
							continue;
						}
						reservedTable.Insert<byte[]>(tx, (int)keyInfo.KeyPath.Indexes.Last(), bytes);
						await RefillAvailable(tx, strategy, derivationFeature);
						await tx.CommitAsync();
					}
					return keyInfo;
				}
				return null;
			}
		}

		private async Task RefillAvailable(NBXplorerDBContext tx, DerivationStrategyBase strategy, DerivationFeature derivationFeature)
		{
			await tx.CommitAsync();
			var availableTable = GetAvailableKeysIndex(strategy, derivationFeature);
			var highestTable = GetHighestPathIndex(strategy, derivationFeature);
			var currentlyAvailable = availableTable.Count(tx);
			if(currentlyAvailable >= MinPoolSize)
				return;
			int highestGenerated = -1;
			int generatedCount = 0;
			var row = await highestTable.Select<int>(tx, 0);
			if(row != null && row.Exists)
				highestGenerated = row.Value;
			var feature = strategy.GetLineFor(derivationFeature);
			while(currentlyAvailable + generatedCount < MaxPoolSize)
			{
				generatedCount++;
				var index = highestGenerated + generatedCount;
				var derivation = feature.Derive((uint)index);
				var info = new KeyPathInformation()
				{
					ScriptPubKey = derivation.ScriptPubKey,
					Redeem = derivation.Redeem,
					DerivationStrategy = strategy,
					Feature = derivationFeature,
					KeyPath = DerivationStrategyBase.GetKeyPath(derivationFeature).Derive(index, false)
				};
				var bytes = ToBytes(info);
				GetScriptsIndex(info.ScriptPubKey).Insert(tx, $"{strategy.GetHash()}-{derivationFeature}", bytes);
				availableTable.Insert(tx, index, bytes);
			}
			if(generatedCount != 0)
				highestTable.Insert(tx, 0, highestGenerated + generatedCount);
		}

		class TimeStampedTransaction : IBitcoinSerializable
		{

			public TimeStampedTransaction()
			{

			}
			public TimeStampedTransaction(byte[] hex)
			{
				this.ReadWrite(hex);
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
				if(stream.Serializing || stream.Inner.Position != stream.Inner.Length)
					stream.ReadWrite(ref _TimeStamp);
			}
		}
		public async Task<List<SavedTransaction>> SaveTransactions(DateTimeOffset now, NBitcoin.Transaction[] transactions, uint256 blockHash)
		{
			var result = new List<SavedTransaction>();
			transactions = transactions.Distinct().ToArray();
			if(transactions.Length == 0)
				return result;

			using(var tx = _ContextFactory.CreateContext())
			{
				var date = NBitcoin.Utils.DateTimeToUnixTime(now);
				foreach(var btx in transactions)
				{
					var timestamped = new TimeStampedTransaction(btx, date);
					var key = blockHash == null ? "0" : blockHash.ToString();
					var value = timestamped.ToBytes();
					tx.Insert($"{_Suffix}tx-" + btx.GetHash().ToString(), key, value);
					result.Add(ToSavedTransaction(key, value));
				}
				await tx.CommitAsync();
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
			using(var tx = _ContextFactory.CreateContext())
			{
				foreach(var row in (await tx.SelectForward<byte[]>($"{_Suffix}tx-" + txid.ToString())))
				{
					SavedTransaction t = ToSavedTransaction(row.Key, row.Value);
					saved.Add(t);
				}
			}
			return saved.ToArray();
		}

		private static SavedTransaction ToSavedTransaction(string key, byte[] value)
		{
			SavedTransaction t = new SavedTransaction();
			if(key.Length != 1)
				t.BlockHash = new uint256(key);
			var timeStamped = new TimeStampedTransaction(value);
			t.Transaction = timeStamped.Transaction;
			t.Timestamp = NBitcoin.Utils.UnixTimeToDateTime(timeStamped.TimeStamp);
			t.Transaction.PrecomputeHash(true, false);
			return t;
		}

		public async Task<MultiValueDictionary<Script, KeyPathInformation>> GetKeyInformations(Script[] scripts)
		{
			MultiValueDictionary<Script, KeyPathInformation> result = new MultiValueDictionary<Script, KeyPathInformation>();
			if(scripts.Length == 0)
				return result;

			using(var tx = _ContextFactory.CreateContext())
			{
				tx.ValuesLazyLoadingIsOn = false;
				foreach(var script in scripts)
				{
					var table = GetScriptsIndex(script);
					var keyInfos = (await table.SelectForwardSkip<byte[]>(tx, 0))
										.Select(r => ToObject<KeyPathInformation>(r.Value))
										.Select(r =>
										{
											r.Address = r.ScriptPubKey.GetDestinationAddress(Network.NBitcoinNetwork).ToString();
											return r;
										})
										// Because xpub are mutable (several xpub map to same script)
										// an attacker could generate lot's of xpub mapping to the same script
										// and this would blow up here. This we take only 5 results max.
										.Take(5)
										.ToArray();
					result.AddRange(script, keyInfos);
				}
			}

			return result;
		}

		public Serializer Serializer
		{
			get; private set;
		}
		private T ToObject<T>(byte[] value)
		{
			return Serializer.ToObject<T>(Unzip(value));
		}
		private byte[] ToBytes<T>(T obj)
		{
			return Zip(Serializer.ToString<T>(obj));
		}

		private byte[] Zip(string unzipped)
		{
			MemoryStream ms = new MemoryStream();
			using(GZipStream gzip = new GZipStream(ms, CompressionMode.Compress))
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
			using(GZipStream gzip = new GZipStream(ms, CompressionMode.Decompress))
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

		public async Task MarkAsUsed(KeyPathInformation[] infos)
		{
			if(infos.Length == 0)
				return;
			using(var tx = _ContextFactory.CreateContext())
			{
				tx.ValuesLazyLoadingIsOn = false;
				foreach(var info in infos)
				{
					var availableIndex = GetAvailableKeysIndex(info.DerivationStrategy, info.Feature);
					var reservedIndex = GetReservedKeysIndex(info.DerivationStrategy, info.Feature);
					var index = (int)info.KeyPath.Indexes.Last();
					var row = await availableIndex.Select<byte[]>(tx, index);
					if(row != null && row.Exists)
					{
						availableIndex.RemoveKey(tx, index);
					}
					row = await reservedIndex.Select<byte[]>(tx, index);
					if(row != null && row.Exists)
						reservedIndex.RemoveKey(tx, index);
					await RefillAvailable(tx, info.DerivationStrategy, info.Feature);
				}
				await tx.CommitAsync();
			}
		}

		public async Task<TrackedTransaction[]> GetTransactions(DerivationStrategyBase pubkey)
		{
			var table = GetTransactionsIndex(pubkey);

			bool needUpdate = false;
			Dictionary<uint256, long> firstSeenList = new Dictionary<uint256, long>();

			var transactions = new List<TransactionMatchData>();
			using(var tx = _ContextFactory.CreateContext())
			{
				tx.ValuesLazyLoadingIsOn = false;
				foreach(var row in await table.SelectForwardSkip<byte[]>(tx, 0))
				{
					if(row == null || !row.Exists)
						continue;
					MemoryStream ms = new MemoryStream(row.Value);
					BitcoinStream bs = new BitcoinStream(ms, false);

					TransactionMatchData data = new TransactionMatchData();
					bs.ReadWrite(ref data);
					data.Transaction.PrecomputeHash(true, true);
					var blockHash = row.Key.Split(':')[1];
					if(blockHash.Length != 0)
						data.BlockHash = new uint256(blockHash);
					transactions.Add(data);

					if(data.NeedUpdate)
						needUpdate = true;

					long firstSeen;
					var hash = data.Transaction.GetHash();
					if(firstSeenList.TryGetValue(data.Transaction.GetHash(), out firstSeen))
					{
						if(firstSeen > data.FirstSeenTickCount)
							firstSeenList[hash] = firstSeen;
					}
					else
					{
						firstSeenList.Add(hash, data.FirstSeenTickCount);
					}
				}
			}

			foreach(var tx in transactions)
			{
				if(tx.FirstSeenTickCount != firstSeenList[tx.Transaction.GetHash()])
				{
					needUpdate = true;
					tx.NeedUpdate = true;
					tx.FirstSeenTickCount = firstSeenList[tx.Transaction.GetHash()];
				}
			}

			// This is legacy data, need an update
			if(needUpdate)
			{
				foreach(var data in transactions.Where(t => t.NeedUpdate && t.TransactionMatch == null))
				{
					data.TransactionMatch = (await this.GetMatches(data.Transaction))
											  .Where(m => m.DerivationStrategy.Equals(pubkey))
											  .Select(m => new TransactionMiniMatch(m))
											  .First();
				}

				using(var tx = _ContextFactory.CreateContext())
				{
					foreach(var data in transactions.Where(t => t.NeedUpdate))
					{
						table.Insert(tx, data.GetRowKey(), data.ToBytes());
					}
					await tx.CommitAsync();
				}
			}

			return transactions.Select(c => new TrackedTransaction()
			{
				BlockHash = c.BlockHash,
				Transaction = c.Transaction,
				Inserted = c.TickCount == 0 ? NBitcoin.Utils.UnixTimeToDateTime(0) : new DateTimeOffset((long)c.TickCount, TimeSpan.Zero),
				FirstSeen = c.FirstSeenTickCount == 0 ? NBitcoin.Utils.UnixTimeToDateTime(0) : new DateTimeOffset((long)c.FirstSeenTickCount, TimeSpan.Zero),
				TransactionMatch = c.TransactionMatch
			}).ToArray();
		}

		public class TransactionMiniKeyInformation : IBitcoinSerializable
		{
			public TransactionMiniKeyInformation()
			{

			}
			public TransactionMiniKeyInformation(KeyPathInformation keyInformation)
			{
				_KeyPath = keyInformation.KeyPath;
				_ScriptPubKey = keyInformation.ScriptPubKey;
			}



			Script _ScriptPubKey;
			public Script ScriptPubKey
			{
				get
				{
					return _ScriptPubKey;
				}
				set
				{
					_ScriptPubKey = value;
				}
			}

			KeyPath _KeyPath;
			public KeyPath KeyPath
			{
				get
				{
					return _KeyPath;
				}
				set
				{
					_KeyPath = value;
				}
			}

			public void ReadWrite(BitcoinStream stream)
			{
				stream.ReadWrite(ref _ScriptPubKey);
				if(stream.Serializing)
				{
					stream.ReadWrite((byte)_KeyPath.Indexes.Length);
					foreach(var index in _KeyPath.Indexes)
					{
						stream.ReadWrite(index);
					}
				}
				else
				{
					byte len = 0;
					stream.ReadWrite(ref len);
					var indexes = new uint[len];
					for(int i = 0; i < len; i++)
					{
						uint index = 0;
						stream.ReadWrite(ref index);
						indexes[i] = index;
					}
					_KeyPath = new KeyPath(indexes);
				}
			}
		}

		public class TransactionMiniMatch : IBitcoinSerializable
		{

			public TransactionMiniMatch()
			{

			}
			public TransactionMiniMatch(TransactionMatch match)
			{
				Inputs = match.Inputs.Select(o => new TransactionMiniKeyInformation(o)).ToArray();
				Outputs = match.Outputs.Select(o => new TransactionMiniKeyInformation(o)).ToArray();
			}


			TransactionMiniKeyInformation[] _Outputs;
			public TransactionMiniKeyInformation[] Outputs
			{
				get
				{
					return _Outputs;
				}
				set
				{
					_Outputs = value;
				}
			}


			TransactionMiniKeyInformation[] _Inputs;
			public TransactionMiniKeyInformation[] Inputs
			{
				get
				{
					return _Inputs;
				}
				set
				{
					_Inputs = value;
				}
			}

			public void ReadWrite(BitcoinStream stream)
			{
				stream.ReadWrite(ref _Inputs);
				stream.ReadWrite(ref _Outputs);
			}
		}

		class TransactionMatchData : IBitcoinSerializable
		{

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


			long _TickCount;
			public long TickCount
			{
				get
				{
					return _TickCount;
				}
				set
				{
					_TickCount = value;
				}
			}


			long _FirstSeenTickCount;
			public long FirstSeenTickCount
			{
				get
				{
					return _FirstSeenTickCount;
				}
				set
				{
					_FirstSeenTickCount = value;
				}
			}


			TransactionMiniMatch _TransactionMatch;
			public TransactionMiniMatch TransactionMatch
			{
				get
				{
					return _TransactionMatch;
				}
				set
				{
					_TransactionMatch = value;
				}
			}

			public bool NeedUpdate
			{
				get; set;
			}
			public void ReadWrite(BitcoinStream stream)
			{
				stream.ReadWrite(ref _Transaction);
				if(stream.Serializing || stream.Inner.Position != stream.Inner.Length)
				{
					stream.ReadWrite(ref _TickCount);
					// We always with FirstSeenTickCount to be at least TickCount
					if(!stream.Serializing)
						_FirstSeenTickCount = _TickCount;
				}
				else
				{
					NeedUpdate = true;
				}
				if(stream.Serializing || stream.Inner.Position != stream.Inner.Length)
				{
					stream.ReadWrite(ref _TransactionMatch);
				}
				else
				{
					NeedUpdate = true;
				}
				if(stream.Serializing || stream.Inner.Position != stream.Inner.Length)
				{
					stream.ReadWrite(ref _FirstSeenTickCount);
				}
				else
				{
					NeedUpdate = true;
				}
			}

			public uint256 BlockHash
			{
				get; set;
			}

			internal string GetRowKey()
			{
				return $"{Transaction.GetHash()}:{BlockHash}";
			}
		}

		public async Task SaveMatches(DateTimeOffset now, MatchedTransaction[] transactions)
		{
			if(transactions.Length == 0)
				return;
			var groups = transactions.GroupBy(i => i.Match.DerivationStrategy);

			using(var tx = _ContextFactory.CreateContext())
			{
				foreach(var group in groups)
				{
					var table = GetTransactionsIndex(group.Key);
					foreach(var value in group)
					{
						var ticksCount = now.UtcTicks;
						var ms = new MemoryStream();
						BitcoinStream bs = new BitcoinStream(ms, true);
						TransactionMatchData data = new TransactionMatchData()
						{
							Transaction = value.Match.Transaction,
							TickCount = ticksCount,
							FirstSeenTickCount = ticksCount,
							TransactionMatch = new TransactionMiniMatch(value.Match),
							BlockHash = value.BlockId
						};
						bs.ReadWrite(data);
						table.Insert(tx, data.GetRowKey(), ms.ToArrayEfficient());
					}
				}
				await tx.CommitAsync();
			}
		}

		private static Script[] GetScripts(TransactionMatch value)
		{
			return value.Outputs.Select(m => m.ScriptPubKey).Concat(value.Inputs.Select(m => m.ScriptPubKey)).ToArray();
		}

		public async Task CleanTransactions(DerivationStrategyBase pubkey, List<TrackedTransaction> cleanList)
		{
			if(cleanList == null || cleanList.Count == 0)
				return;
			var table = GetTransactionsIndex(pubkey);
			using(var tx = _ContextFactory.CreateContext())
			{
				foreach(var tracked in cleanList)
				{
					var k = tracked.GetRowKey();
					table.RemoveKey(tx, k);
				}
				await tx.CommitAsync();
			}
		}

		public async Task Track(DerivationStrategyBase strategy)
		{
			using(var tx = _ContextFactory.CreateContext())
			{
				tx.ValuesLazyLoadingIsOn = false;
				foreach(var feature in Enum.GetValues(typeof(DerivationFeature)).Cast<DerivationFeature>())
				{
					await RefillAvailable(tx, strategy, feature);
				}
				await tx.CommitAsync();
			}
		}
	}
}
