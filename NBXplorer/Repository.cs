﻿using DBreeze;
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
using DBreeze.Utils;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using DBreeze.Exceptions;
using NBXplorer.Logging;
using NBXplorer.Configuration;
using static NBXplorer.RepositoryProvider;
using static NBXplorer.Repository;

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
		internal class CustomThreadPool : IDisposable
		{
			CancellationTokenSource _Cancel = new CancellationTokenSource();
			TaskCompletionSource<bool> _Exited;
			int _ExitedCount = 0;
			Thread[] _Threads;
			Exception _UnhandledException;
			BlockingCollection<(Action, TaskCompletionSource<object>)> _Actions = new BlockingCollection<(Action, TaskCompletionSource<object>)>(new ConcurrentQueue<(Action, TaskCompletionSource<object>)>());

			public CustomThreadPool(int threadCount, string threadName)
			{
				if(threadCount <= 0)
					throw new ArgumentOutOfRangeException(nameof(threadCount));
				_Exited = new TaskCompletionSource<bool>();
				_Threads = Enumerable.Range(0, threadCount).Select(_ => new Thread(RunLoop) { Name = threadName }).ToArray();
				foreach(var t in _Threads)
					t.Start();
			}

			public void Do(Action act)
			{
				DoAsync(act).GetAwaiter().GetResult();
			}

			public T Do<T>(Func<T> act)
			{
				return DoAsync(act).GetAwaiter().GetResult();
			}

			public async Task<T> DoAsync<T>(Func<T> act)
			{
				TaskCompletionSource<object> done = new TaskCompletionSource<object>();
				_Actions.Add((() =>
				{
					try
					{
						done.TrySetResult(act());
					}
					catch(Exception ex) { done.TrySetException(ex); }
				}
				, done));
				return (T)(await done.Task.ConfigureAwait(false));
			}

			public Task DoAsync(Action act)
			{
				return DoAsync<object>(() =>
				{
					act();
					return null;
				});
			}

			void RunLoop()
			{
				try
				{
					foreach(var act in _Actions.GetConsumingEnumerable(_Cancel.Token))
					{
						act.Item1();
					}
				}
				catch(OperationCanceledException) when(_Cancel.IsCancellationRequested) { }
				catch(Exception ex)
				{
					_Cancel.Cancel();
					_UnhandledException = ex;
					Logs.Explorer.LogError(ex, "Unexpected exception thrown by Repository");
				}
				if(Interlocked.Increment(ref _ExitedCount) == _Threads.Length)
				{
					foreach(var action in _Actions)
					{
						try
						{
							action.Item2.TrySetCanceled();
						}
						catch { }
					}
					_Exited.TrySetResult(true);
				}
			}

			public void Dispose()
			{
				_Cancel.Cancel();
				_Exited.Task.GetAwaiter().GetResult();
			}
		}
		internal class EngineAccessor : IDisposable
		{
			private DBreezeEngine _Engine;
			private CustomThreadPool _Pool;
			Timer _Renew;
			string directory;
			public EngineAccessor(string directory)
			{
				this.directory = directory;
				try
				{
					_Pool = new CustomThreadPool(1, "Repository");
					RenewEngine();
				}
				catch { Dispose(); throw; }
				_Renew = new Timer((o) =>
				{
					try
					{
						RenewEngine();
					}
					catch { }
				});
				_Renew.Change(0, (int)TimeSpan.FromSeconds(60).TotalMilliseconds);
			}

			private void RenewEngine()
			{
				_Pool.Do(() =>
				{
					DisposeEngine();
					int tried = 0;
					while(true)
					{
						try
						{
							_Engine = new DBreezeEngine(directory);
							break;
						}
						catch when(tried < 5)
						{
							tried++;
							Thread.Sleep(5000);
						}
					}
					_Tx = _Engine.GetTransaction();
				});
			}

			private void DisposeEngine()
			{
				if(_Tx != null)
				{
					try
					{
						_Tx.Dispose();
					}
					catch { }
					_Tx = null;
				}
				if(_Engine != null)
				{
					try
					{
						_Engine.Dispose();
					}
					catch { }
					_Engine = null;
				}
			}

			DBreeze.Transactions.Transaction _Tx;
			public void Do(Action<DBreeze.Transactions.Transaction> act)
			{
				AssertNotDisposed();
				_Pool.Do(() =>
				{
					AssertNotDisposed();
					RetryIfFailed(() =>
					{
						act(_Tx);
					});
				});
			}

			void RetryIfFailed(Action act)
			{
				try
				{
					act();
				}
				catch(Exception ex)
				{
					Logs.Explorer.LogError(ex, "Unexpected DBreeze error");
					RenewEngine();
					act();
				}
			}

			T RetryIfFailed<T>(Func<T> act)
			{
				try
				{
					return act();
				}
				catch(DBreezeException)
				{
					RenewEngine();
					return act();
				}
			}

			public Task DoAsync(Action<DBreeze.Transactions.Transaction> act)
			{
				AssertNotDisposed();
				return _Pool.DoAsync(() =>
				{
					AssertNotDisposed();
					RetryIfFailed(() =>
					{
						act(_Tx);
					});
				});
			}

			public Task<T> DoAsync<T>(Func<DBreeze.Transactions.Transaction, T> act)
			{
				AssertNotDisposed();
				return _Pool.DoAsync(() =>
				{
					AssertNotDisposed();
					return RetryIfFailed(() =>
					{
						return act(_Tx);
					});
				});
			}

			public T Do<T>(Func<DBreeze.Transactions.Transaction, T> act)
			{
				AssertNotDisposed();
				return _Pool.Do<T>(() =>
				{
					AssertNotDisposed();
					return RetryIfFailed(() =>
					{
						return act(_Tx);
					});
				});
			}

			void AssertNotDisposed()
			{
				if(_Disposed)
					throw new ObjectDisposedException("EngineAccessor");
			}
			bool _Disposed;
			public void Dispose()
			{
				if(!_Disposed)
				{
					_Disposed = true;
					if(_Renew != null)
						_Renew.Dispose();
					_Pool.DoAsync(() => DisposeEngine()).GetAwaiter().GetResult();
					_Pool.Dispose();
				}
			}
		}

		EngineAccessor _Engine;
		Dictionary<string, Repository> _Repositories = new Dictionary<string, Repository>();

		public RepositoryProvider(NBXplorerNetworkProvider networks, ExplorerConfiguration configuration)
		{
			var directory = Path.Combine(configuration.DataDir, "db");
			if(!Directory.Exists(directory))
				Directory.CreateDirectory(directory);
			_Engine = new EngineAccessor(directory);
			foreach(var net in networks.GetAll())
			{
				var settings = configuration.ChainConfigurations.FirstOrDefault(c => c.CryptoCode == net.CryptoCode);
				if(settings != null)
				{
					var repo = new Repository(_Engine, net);
					repo.MaxPoolSize = configuration.MaxGapSize;
					repo.MinPoolSize = configuration.MinGapSize;
					_Repositories.Add(net.CryptoCode, repo);
					if(settings.Rescan)
					{
						Logs.Configuration.LogInformation($"Rescanning the {net.CryptoCode} chain...");
						repo.SetIndexProgress(null);
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
			_Engine.Dispose();
		}
	}

	public class Repository
	{


		public void Ping()
		{
			_Engine.Do((tx) =>
			{
			});
		}

		public void CancelReservation(DerivationStrategyBase strategy, KeyPath[] keyPaths)
		{
			if(keyPaths.Length == 0)
				return;
			_Engine.Do(tx =>
			{
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
							var data = reserved.Select<byte[]>(tx, key);
							if(data == null || !data.Exists)
								continue;
							reserved.RemoveKey(tx, key);
							available.Insert(tx, key, data.Value);
							needCommit = true;
						}
					}
				}
				if(needCommit)
					tx.Commit();
			});
		}

		class Index
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

			public DBreeze.DataTypes.Row<string, T> Select<T>(DBreeze.Transactions.Transaction tx, int index)
			{
				return tx.Select<string, T>(TableName, $"{PrimaryKey}-{index:D10}");
			}

			public void RemoveKey(DBreeze.Transactions.Transaction tx, int index)
			{
				tx.RemoveKey(TableName, $"{PrimaryKey}-{index:D10}");
			}

			public void RemoveKey(DBreeze.Transactions.Transaction tx, string index)
			{
				tx.RemoveKey(TableName, $"{PrimaryKey}-{index}");
			}

			public IEnumerable<DBreeze.DataTypes.Row<string, T>> SelectForwardSkip<T>(DBreeze.Transactions.Transaction tx, int n)
			{
				return tx.SelectForwardStartsWith<string, T>(TableName, PrimaryKey).Skip(n);
			}

			public void Insert<T>(DBreeze.Transactions.Transaction tx, int key, T value)
			{
				tx.Insert(TableName, $"{PrimaryKey}-{key:D10}", value);
			}
			public void Insert<T>(DBreeze.Transactions.Transaction tx, string key, T value)
			{
				tx.Insert(TableName, $"{PrimaryKey}-{key}", value);
			}

			public int Count(DBreeze.Transactions.Transaction tx)
			{
				return tx.SelectForwardStartsWith<string, byte[]>(TableName, PrimaryKey).Count();
			}
			
			public IEnumerable<DBreeze.DataTypes.Row<string, T>> SelectForward<T>(DBreeze.Transactions.Transaction tx)
			{
				return tx.SelectForward<string, T>(TableName);
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

		EngineAccessor _Engine;
		internal Repository(EngineAccessor engineAccessor, NBXplorerNetwork network)
		{
			if(network == null)
				throw new ArgumentNullException(nameof(network));
			_Network = network;
			Serializer = new Serializer(_Network.NBitcoinNetwork);
			_Network = network;
			_Engine = engineAccessor;
			_Suffix = network.CryptoCode == "BTC" ? "" : network.CryptoCode;
		}
		public string _Suffix;
		public BlockLocator GetIndexProgress()
		{
			return _Engine.Do<BlockLocator>(tx =>
			{
				tx.ValuesLazyLoadingIsOn = false;
				var existingRow = tx.Select<string, byte[]>($"{_Suffix}IndexProgress", "");
				if(existingRow == null || !existingRow.Exists)
					return null;
				BlockLocator locator = new BlockLocator();
				locator.FromBytes(existingRow.Value);
				return locator;
			});
		}

		public void SetIndexProgress(BlockLocator locator)
		{
			_Engine.Do(tx =>
			{
				if(locator == null)
					tx.RemoveKey($"{_Suffix}IndexProgress", "");
				else
					tx.Insert($"{_Suffix}IndexProgress", "", locator.ToBytes());
				tx.Commit();
			});
		}

		public Task<KeyPathInformation> GetUnused(DerivationStrategyBase strategy, DerivationFeature derivationFeature, int n, bool reserve)
		{
			return _Engine.DoAsync<KeyPathInformation>((tx) =>
			{
				tx.ValuesLazyLoadingIsOn = false;
				var availableTable = GetAvailableKeysIndex(strategy, derivationFeature);
				var reservedTable = GetReservedKeysIndex(strategy, derivationFeature);
				var bytes = availableTable.SelectForwardSkip<byte[]>(tx, n).FirstOrDefault()?.Value;
				if(bytes == null)
					return null;
				var keyInfo = ToObject<KeyPathInformation>(bytes);
				if(reserve)
				{
					availableTable.RemoveKey(tx, (int)keyInfo.KeyPath.Indexes.Last());
					reservedTable.Insert<byte[]>(tx, (int)keyInfo.KeyPath.Indexes.Last(), bytes);
					RefillAvailable(tx, strategy, derivationFeature);
					tx.Commit();
				}
				return keyInfo;
			});
		}

		private void RefillAvailable(DBreeze.Transactions.Transaction tx, DerivationStrategyBase strategy, DerivationFeature derivationFeature)
		{
			var availableTable = GetAvailableKeysIndex(strategy, derivationFeature);
			var highestTable = GetHighestPathIndex(strategy, derivationFeature);
			var currentlyAvailable = availableTable.Count(tx);
			if(currentlyAvailable >= MinPoolSize)
				return;

			int highestGenerated = -1;
			int generatedCount = 0;
			var row = highestTable.Select<int>(tx, 0);
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

		public int BatchSize
		{
			get; set;
		} = 100;
		public List<SavedTransaction> SaveTransactions(DateTimeOffset now, NBitcoin.Transaction[] transactions, uint256 blockHash)
		{
			var result = new List<SavedTransaction>();
			transactions = transactions.Distinct().ToArray();
			if(transactions.Length == 0)
				return result;
			foreach(var batch in transactions.Batch(BatchSize))
			{
				_Engine.Do(tx =>
				{
					var date = NBitcoin.Utils.DateTimeToUnixTime(now);
					foreach(var btx in batch)
					{
						var timestamped = new TimeStampedTransaction(btx, date);
						var key = blockHash == null ? "0" : blockHash.ToString();
						var value = timestamped.ToBytes();
						tx.Insert($"{_Suffix}tx-" + btx.GetHash().ToString(), key, value);
						result.Add(ToSavedTransaction(key, value));
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

		public SavedTransaction[] GetSavedTransactions(uint256 txid)
		{
			List<SavedTransaction> saved = new List<SavedTransaction>();
			_Engine.Do(tx =>
			{
				foreach(var row in tx.SelectForward<string, byte[]>($"{_Suffix}tx-" + txid.ToString()))
				{
					SavedTransaction t = ToSavedTransaction(row.Key, row.Value);
					saved.Add(t);
				}
			});
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

		public MultiValueDictionary<Script, KeyPathInformation> GetKeyInformations(Script[] scripts)
		{
			MultiValueDictionary<Script, KeyPathInformation> result = new MultiValueDictionary<Script, KeyPathInformation>();
			if(scripts.Length == 0)
				return result;
			foreach(var batch in scripts.Batch(BatchSize))
			{
				_Engine.Do(tx =>
				{
					tx.ValuesLazyLoadingIsOn = false;
					foreach(var script in batch)
					{
						var table = GetScriptsIndex(script);
						var keyInfos = table.SelectForwardSkip<byte[]>(tx, 0)
											.Select(r => ToObject<KeyPathInformation>(r.Value))
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

		public void MarkAsUsed(KeyPathInformation[] infos)
		{
			if(infos.Length == 0)
				return;
			_Engine.Do(tx =>
			{
				foreach(var info in infos)
				{
					var availableIndex = GetAvailableKeysIndex(info.DerivationStrategy, info.Feature);
					var reservedIndex = GetReservedKeysIndex(info.DerivationStrategy, info.Feature);
					var index = (int)info.KeyPath.Indexes.Last();
					var row = availableIndex.Select<byte[]>(tx, index);
					if(row != null && row.Exists)
					{
						availableIndex.RemoveKey(tx, index);
					}
					row = reservedIndex.Select<byte[]>(tx, index);
					if(row != null && row.Exists)
						reservedIndex.RemoveKey(tx, index);
					RefillAvailable(tx, info.DerivationStrategy, info.Feature);
				}
				tx.Commit();
			});
		}

		public TrackedTransaction[] GetTransactions(DerivationStrategyBase pubkey)
		{
			var table = GetTransactionsIndex(pubkey);

			bool needUpdate = false;
			Dictionary<uint256, long> firstSeenList = new Dictionary<uint256, long>();

			var transactions = _Engine.Do(tx =>
			{
				tx.ValuesLazyLoadingIsOn = false;
				var result = new List<TransactionMatchData>();
				foreach(var row in table.SelectForwardSkip<byte[]>(tx, 0))
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
					result.Add(data);

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
				return result;
			});

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
					data.TransactionMatch = this.GetMatches(data.Transaction)
											  .Where(m => m.DerivationStrategy.Equals(pubkey))
											  .Select(m => new TransactionMiniMatch(m))
											  .First();
				}

				_Engine.Do(tx =>
				{
					foreach(var data in transactions.Where(t => t.NeedUpdate))
					{
						table.Insert(tx, data.GetRowKey(), data.ToBytes());
					}
					tx.Commit();
				});
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

		public void SaveMatches(DateTimeOffset now, MatchedTransaction[] transactions)
		{
			if(transactions.Length == 0)
				return;
			var groups = transactions.GroupBy(i => i.Match.DerivationStrategy);

			_Engine.Do(tx =>
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
				tx.Commit();
			});
		}

		private static Script[] GetScripts(TransactionMatch value)
		{
			return value.Outputs.Select(m => m.ScriptPubKey).Concat(value.Inputs.Select(m => m.ScriptPubKey)).ToArray();
		}

		public void CleanTransactions(DerivationStrategyBase pubkey, List<TrackedTransaction> cleanList)
		{
			if(cleanList == null || cleanList.Count == 0)
				return;
			var table = GetTransactionsIndex(pubkey);
			_Engine.Do(tx =>
			{
				foreach(var tracked in cleanList)
				{
					var k = tracked.GetRowKey();
					table.RemoveKey(tx, k);
				}
				tx.Commit();
			});
		}

		public void Track(DerivationStrategyBase strategy)
		{
			_Engine.Do(tx =>
			{
				foreach(var feature in Enum.GetValues(typeof(DerivationFeature)).Cast<DerivationFeature>())
				{
					RefillAvailable(tx, strategy, feature);
				}
				tx.Commit();
			});
		}
		
		public Task<List<KeyPathInformation>> GetAvailableKeys(
			DerivationStrategyBase derivationStrategy = null,
			DerivationFeature? derivationFeature = null,
			KeyPath keyPath = null,
			Script scriptPubKey = null)
		{
			return _Engine.DoAsync(tx =>
			{
				tx.ValuesLazyLoadingIsOn = false;								
				IEnumerable<KeyPathInformation> keyInfos;
				var available = new Index($"{_Suffix}AvailableKeys", "");
				
				keyInfos = available.SelectForward<byte[]>(tx)
					.Select(r => ToObject<KeyPathInformation>(r.Value))
					.Where(r => derivationStrategy == null || r.DerivationStrategy == derivationStrategy)
					.Where(r => derivationFeature == null || r.Feature == derivationFeature)
					.Where(r => keyPath == null || r.KeyPath == keyPath)
					.Where(r => scriptPubKey == null || r.ScriptPubKey == scriptPubKey)
					;
				return keyInfos.ToList();
			});
		}
	}
}
