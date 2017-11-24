using DBreeze;
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
using NBXplorer.Client;
using NBXplorer.Models;
using System.Threading.Tasks;
using System.Threading;
using NBitcoin.DataEncoders;
using DBreeze.Utils;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using DBreeze.Exceptions;
using NBXplorer.Logging;

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

		internal string GetRowKey()
		{
			return $"{Transaction.GetHash()}:{BlockHash}";
		}
	}

	public class InsertTransaction
	{
		public DerivationStrategyBase DerivationStrategy
		{
			get; set;
		}
		public TrackedTransaction TrackedTransaction
		{
			get; set;
		}
	}

	public class Repository : IDisposable
	{
		class CustomThreadPool : IDisposable
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
		class EngineAccessor : IDisposable
		{
			private DBreezeEngine _Engine;
			private CustomThreadPool _Pool;
			Timer _Renew;
			string directory;
			public EngineAccessor(string directory)
			{
				this.directory = directory;
				_Pool = new CustomThreadPool(1, "Repository");
				RenewEngine();
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
						catch when (tried < 5)
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
					_Renew.Dispose();
					_Pool.DoAsync(() => DisposeEngine()).GetAwaiter().GetResult();
					_Pool.Dispose();
				}
			}
		}

		public Task PingAsync()
		{
			return _Engine.DoAsync((tx) =>
			{
			});
		}

		public Task CancelReservation(DerivationStrategyBase strategy, KeyPath[] keyPaths)
		{
			if(keyPaths.Length == 0)
				return Task.CompletedTask;
			return _Engine.DoAsync(tx =>
			{
				bool needCommit = false;
				var featuresPerKeyPaths =
				new[] { DerivationFeature.Deposit, DerivationFeature.Change }
				.Select(f => (Feature: f, Path: strategy.GetLineFor(f).Path))
				.ToDictionary(o => o.Path, o => o.Feature);

				var groups = keyPaths.Where(k => k.Indexes.Length > 0).GroupBy(k => k.Parent);
				foreach(var group in groups)
				{
					if(featuresPerKeyPaths.TryGetValue(group.Key, out DerivationFeature feature))
					{
						var reserved = Index.GetReservedKeys(strategy, feature);
						var available = Index.GetAvailableKeys(strategy, feature);
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
			private Index(string tableName, string primaryKey)
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

			public static Index GetAvailableKeys(DerivationStrategyBase strategy, DerivationFeature feature)
			{
				return new Index("AvailableKeys", $"{strategy.GetHash()}-{feature}");
			}

			public static Index GetScripts(Script scriptPubKey)
			{
				return new Index("Scripts", $"{scriptPubKey.Hash}");
			}

			public static Index GetHighestPath(DerivationStrategyBase strategy, DerivationFeature feature)
			{
				return new Index("HighestPath", $"{strategy.GetHash()}-{feature}");
			}

			public static Index GetReservedKeys(DerivationStrategyBase strategy, DerivationFeature feature)
			{
				return new Index("ReservedKeys", $"{strategy.GetHash()}-{feature}");
			}

			public static Index GetTransactions(DerivationStrategyBase derivation)
			{
				return new Index("Transactions", $"{derivation.GetHash()}");
			}

			public static Index GetCallbacks(DerivationStrategyBase derivation)
			{
				return new Index("Callbacks", $"{derivation.GetHash()}");
			}

			public static Index GetBlockCallbacks()
			{
				return new Index("BlockCallbacks", "b");
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
		}
		EngineAccessor _Engine;
		Serializer Serializer;
		public Repository(Serializer serializer, string directory)
		{
			if(serializer == null)
				throw new ArgumentNullException(nameof(serializer));
			if(!Directory.Exists(directory))
				Directory.CreateDirectory(directory);

			Serializer = serializer;
			_Engine = new EngineAccessor(directory);
		}
		public BlockLocator GetIndexProgress()
		{
			return _Engine.Do<BlockLocator>(tx =>
			{
				tx.ValuesLazyLoadingIsOn = false;
				var existingRow = tx.Select<string, byte[]>("IndexProgress", "");
				if(existingRow == null || !existingRow.Exists)
					return null;
				BlockLocator locator = new BlockLocator();
				locator.FromBytes(existingRow.Value);
				return locator;
			});
		}

		public Task<Uri[]> GetBlockCallbacks()
		{
			return _Engine.DoAsync(tx =>
			{
				var index = Index.GetBlockCallbacks();
				return index.SelectForwardSkip<string>(tx, 0)
				.Where(r => r.Exists)
				.Select(r => new Uri(r.Key.Substring(index.PrimaryKey.Length + 1), UriKind.Absolute))
				.ToArray();
			});
		}
		public Task<Uri[]> GetCallbacks(DerivationStrategyBase strategy)
		{
			return _Engine.DoAsync(tx =>
			{
				var index = Index.GetCallbacks(strategy);
				return index.SelectForwardSkip<string>(tx, 0)
				.Where(r => r.Exists)
				.Select(r => new Uri(r.Key.Substring(index.PrimaryKey.Length + 1), UriKind.Absolute))
				.ToArray();
			});
		}

		public Task AddCallback(DerivationStrategyBase strategy, Uri callback)
		{
			return _Engine.DoAsync(tx =>
			{
				var index = Index.GetCallbacks(strategy);
				index.Insert(tx, callback.AbsoluteUri, 0);
				tx.Commit();
			});
		}

		public Task AddBlockCallback(Uri callback)
		{
			return _Engine.DoAsync(tx =>
			{
				var index = Index.GetBlockCallbacks();
				index.Insert(tx, callback.AbsoluteUri, 0);
				tx.Commit();
			});
		}


		public void SetIndexProgress(BlockLocator locator)
		{
			_Engine.Do(tx =>
			{
				if(locator == null)
					tx.RemoveKey("IndexProgress", "");
				else
					tx.Insert("IndexProgress", "", locator.ToBytes());
				tx.Commit();
			});
		}

		public Task<KeyPathInformation> GetUnused(DerivationStrategyBase strategy, DerivationFeature derivationFeature, int n, bool reserve)
		{
			return _Engine.DoAsync<KeyPathInformation>((tx) =>
			{
				tx.ValuesLazyLoadingIsOn = false;
				var availableTable = Index.GetAvailableKeys(strategy, derivationFeature);
				var reservedTable = Index.GetReservedKeys(strategy, derivationFeature);
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
			var availableTable = Index.GetAvailableKeys(strategy, derivationFeature);
			var highestTable = Index.GetHighestPath(strategy, derivationFeature);
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
					KeyPath = feature.Path.Derive(index, false)
				};
				var bytes = ToBytes(info);
				Index.GetScripts(info.ScriptPubKey).Insert(tx, $"{strategy.GetHash()}-{derivationFeature}", bytes);
				availableTable.Insert(tx, index, bytes);
			}
			if(generatedCount != 0)
				highestTable.Insert(tx, 0, highestGenerated + generatedCount);
		}

		public void SaveTransactions(Transaction[] transactions, uint256 blockHash)
		{
			transactions = transactions.Distinct().ToArray();
			if(transactions.Length == 0)
				return;
			_Engine.Do(tx =>
			{
				foreach(var btx in transactions)
				{
					tx.Insert("tx-" + btx.GetHash().ToString(), blockHash == null ? "0" : blockHash.ToString(), btx.ToBytes());
				}
				tx.Commit();
			});
		}

		public class SavedTransaction
		{
			public Transaction Transaction
			{
				get; set;
			}
			public uint256 BlockHash
			{
				get; set;
			}
		}

		public SavedTransaction[] GetSavedTransactions(uint256 txid)
		{
			List<SavedTransaction> saved = new List<SavedTransaction>();
			_Engine.Do(tx =>
			{
				foreach(var row in tx.SelectForward<string, byte[]>("tx-" + txid.ToString()))
				{
					SavedTransaction t = new SavedTransaction();
					if(row.Key.Length != 1)
						t.BlockHash = new uint256(row.Key);
					t.Transaction = new Transaction(row.Value);
					saved.Add(t);
				}
			});
			return saved.ToArray();
		}

		public Task<KeyPathInformation[][]> GetKeyInformations(Script[] scripts)
		{
			if(scripts.Length == 0)
				return Task.FromResult(new KeyPathInformation[0][]);
			return _Engine.DoAsync(tx =>
			{
				List<KeyPathInformation[]> result = new List<KeyPathInformation[]>();
				tx.ValuesLazyLoadingIsOn = false;
				foreach(var script in scripts)
				{
					var table = Index.GetScripts(script);
					result.Add(table.SelectForwardSkip<byte[]>(tx, 0).Select(r => ToObject<KeyPathInformation>(r.Value)).ToArray());
				}
				return result.ToArray();
			});
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

		public const int MinPoolSize = 20;
		public const int MaxPoolSize = 30;

		public Task MarkAsUsedAsync(KeyPathInformation[] infos)
		{
			if(infos.Length == 0)
				return Task.CompletedTask;
			return _Engine.DoAsync(tx =>
			{
				foreach(var info in infos)
				{
					var availableIndex = Index.GetAvailableKeys(info.DerivationStrategy, info.Feature);
					var reservedIndex = Index.GetReservedKeys(info.DerivationStrategy, info.Feature);
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
			var table = Index.GetTransactions(pubkey);
			return _Engine.Do(tx =>
			{
				var result = new List<TrackedTransaction>();
				foreach(var row in table.SelectForwardSkip<byte[]>(tx, 0))
				{
					if(row == null || !row.Exists)
						continue;
					MemoryStream ms = new MemoryStream(row.Value);
					BitcoinStream bs = new BitcoinStream(ms, false);
					Transaction transaction = null;
					bs.ReadWrite<Transaction>(ref transaction);
					ulong ticksCount = 0;
					if(ms.Position < ms.Length)
						bs.ReadWrite(ref ticksCount);
					transaction.CacheHashes();
					var blockHash = row.Key.Split(':')[1];
					var tracked = new TrackedTransaction();
					if(blockHash.Length != 0)
						tracked.BlockHash = new uint256(blockHash);
					tracked.Transaction = transaction;
					tracked.Inserted = new DateTimeOffset((long)ticksCount, TimeSpan.Zero);
					result.Add(tracked);
				}
				return result.ToArray();
			});
		}


		public void SaveMatches(InsertTransaction[] transactions)
		{
			if(transactions.Length == 0)
				return;
			var groups = transactions.GroupBy(i => i.DerivationStrategy);

			_Engine.Do(tx =>
			{
				foreach(var group in groups)
				{
					var table = Index.GetTransactions(group.Key);
					foreach(var value in group)
					{
						var ticksCount = DateTimeOffset.UtcNow.UtcTicks;
						var ms = new MemoryStream();
						BitcoinStream bs = new BitcoinStream(ms, true);
						bs.ReadWrite(value.TrackedTransaction.Transaction);
						bs.ReadWrite(ticksCount);
						table.Insert(tx, value.TrackedTransaction.GetRowKey(), ms.ToArrayEfficient());
					}
				}
				tx.Commit();
			});
		}

		public void CleanTransactions(DerivationStrategyBase pubkey, List<TrackedTransaction> cleanList)
		{
			if(cleanList == null || cleanList.Count == 0)
				return;
			var table = Index.GetTransactions(pubkey);
			_Engine.Do(tx =>
			{
				foreach(var tracked in cleanList)
				{
					table.RemoveKey(tx, tracked.GetRowKey());
				}
				tx.Commit();
			});
		}

		public void Dispose()
		{
			_Engine.Dispose();
		}

		public Task TrackAsync(DerivationStrategyBase strategy)
		{
			return _Engine.DoAsync(tx =>
			{
				foreach(var feature in new[] { DerivationFeature.Change, DerivationFeature.Deposit })
				{
					RefillAvailable(tx, strategy, feature);
				}
				tx.Commit();
			});
		}
	}
}
