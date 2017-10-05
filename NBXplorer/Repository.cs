using DBreeze;
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

		internal string GetRowKey()
		{
			return $"{Transaction.GetHash()}:{BlockHash}";
		}
	}

	public class InsertTransaction
	{
		public DerivationStrategyBase PubKey
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
				catch(Exception ex) { _Cancel.Cancel(); _UnhandledException = ex; }
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
			private DBreezeEngine engine;
			private CustomThreadPool _Pool;
			public EngineAccessor(DBreezeEngine engine)
			{
				this.engine = engine;
				_Pool = new CustomThreadPool(1, "Repository");
			}

			public void Do(Action<DBreeze.Transactions.Transaction> act)
			{
				_Pool.Do(() =>
				{
					using(var tx = engine.GetTransaction())
					{
						act(tx);
					}
				});
			}

			public Task DoAsync(Action<DBreeze.Transactions.Transaction> act)
			{
				return _Pool.DoAsync(() =>
				{
					using(var tx = engine.GetTransaction())
					{
						act(tx);
					}
				});
			}

			public Task<T> DoAsync<T>(Func<DBreeze.Transactions.Transaction, T> act)
			{
				return _Pool.DoAsync(() =>
				{
					using(var tx = engine.GetTransaction())
					{
						return act(tx);
					}
				});
			}

			public T Do<T>(Func<DBreeze.Transactions.Transaction, T> act)
			{
				return _Pool.Do<T>(() =>
				{
					using(var tx = engine.GetTransaction())
					{
						return act(tx);
					}
				});
			}

			public void Dispose()
			{
				_Pool.Dispose();
				engine.Dispose();
			}
		}

		class Tables
		{
			public static string GetAvailableKeys(DerivationStrategyBase strategy, DerivationFeature feature)
			{
				return $"A-{strategy.GetHash()}-{feature}";
			}

			public static string GetScripts(Script scriptPubKey)
			{
				return $"S-{scriptPubKey.Hash.ToString()}";
			}

			public static string GetHighestPath(DerivationStrategyBase strategy, DerivationFeature feature)
			{
				return $"K-{strategy.GetHash()}-{feature}";
			}

			public static string GetReservedKeys(DerivationStrategyBase strategy, DerivationFeature feature)
			{
				return $"R-{strategy.GetHash()}-{feature}";
			}

			public static string GetUsedKeys(DerivationStrategyBase strategy, DerivationFeature feature)
			{
				return $"U-{strategy.GetHash()}-{feature}";
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
			_Engine = new EngineAccessor(new DBreezeEngine(directory));
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
				RefillAvailable(tx, strategy, derivationFeature);
				var availableTable = Tables.GetAvailableKeys(strategy, derivationFeature);
				var reservedTable = Tables.GetReservedKeys(strategy, derivationFeature);
				var bytes = tx.SelectForwardSkip<int, byte[]>(availableTable, (ulong)n).FirstOrDefault()?.Value;
				if(bytes == null)
					return null;
				var keyInfo = ToObject<KeyPathInformation>(bytes);
				if(reserve)
				{
					tx.RemoveKey(availableTable, (int)keyInfo.KeyPath.Indexes.Last());
					tx.Insert(reservedTable, (int)keyInfo.KeyPath.Indexes.Last(), ToBytes(keyInfo));
					RefillAvailable(tx, strategy, derivationFeature);
				}
				tx.Commit();
				return keyInfo;
			});
		}

		private void RefillAvailable(DBreeze.Transactions.Transaction tx, DerivationStrategyBase strategy, DerivationFeature derivationFeature)
		{
			var availableTable = Tables.GetAvailableKeys(strategy, derivationFeature);
			var highestTable = Tables.GetHighestPath(strategy, derivationFeature);
			var currentlyAvailable = (int)tx.Count(availableTable);
			if(currentlyAvailable >= MinPoolSize)
				return;

			int highestGenerated = -1;
			int generatedCount = 0;
			var row = tx.Select<string, int>(highestTable, "value");
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
				tx.Insert(Tables.GetScripts(info.ScriptPubKey), Encoders.Hex.EncodeData(RandomUtils.GetBytes(20)), bytes);
				tx.Insert(availableTable, index, bytes);
			}
			if(generatedCount != 0)
				tx.Insert(highestTable, "value", highestGenerated + generatedCount);
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

		public Task<KeyPathInformation[]> GetKeyInformations(Script script)
		{
			return _Engine.DoAsync(tx =>
			{
				tx.ValuesLazyLoadingIsOn = false;
				var table = Tables.GetScripts(script);
				return tx.SelectForward<string, byte[]>(table).Select(r => ToObject<KeyPathInformation>(r.Value)).ToArray();
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

		public Task MarkAsUsedAsync(KeyPathInformation info)
		{
			return _Engine.DoAsync(tx =>
			{
				var availableTable = Tables.GetAvailableKeys(info.DerivationStrategy, info.Feature);
				var reservedTable = Tables.GetReservedKeys(info.DerivationStrategy, info.Feature);
				var index = (int)info.KeyPath.Indexes.Last();
				var row = tx.Select<int, byte[]>(availableTable, index);
				if(row != null && row.Exists)
				{
					tx.RemoveKey(availableTable, index);
				}
				row = tx.Select<int, byte[]>(reservedTable, index);
				if(row != null && row.Exists)
				{
					tx.RemoveKey(availableTable, index);
				}
				RefillAvailable(tx, info.DerivationStrategy, info.Feature);
				tx.Commit();
			});
		}

		public TrackedTransaction[] GetTransactions(DerivationStrategyBase pubkey)
		{
			var tableName = $"T-{pubkey.GetHash()}";

			return _Engine.Do(tx =>
			{
				var result = new List<TrackedTransaction>();
				foreach(var row in tx.SelectForward<string, byte[]>(tableName))
				{
					if(row == null || !row.Exists)
						continue;
					var transaction = new Transaction(row.Value);
					transaction.CacheHashes();
					var blockHash = row.Key.Split(':')[1];
					var tracked = new TrackedTransaction();
					if(blockHash.Length != 0)
						tracked.BlockHash = new uint256(blockHash);
					tracked.Transaction = transaction;
					result.Add(tracked);
				}
				return result.ToArray();
			});
		}


		public void InsertTransactions(InsertTransaction[] transactions)
		{
			if(transactions.Length == 0)
				return;
			var groups = transactions.GroupBy(i => $"T-{i.PubKey.GetHash()}");

			_Engine.Do(tx =>
			{
				foreach(var group in groups)
				{
					foreach(var value in group)
						tx.Insert(group.Key, value.TrackedTransaction.GetRowKey(), value.TrackedTransaction.Transaction.ToBytes());
				}
				tx.Commit();
			});
		}

		public void CleanTransactions(DerivationStrategyBase pubkey, List<TrackedTransaction> cleanList)
		{
			if(cleanList == null || cleanList.Count == 0)
				return;
			var tableName = $"T-{pubkey.GetHash()}";
			_Engine.Do(tx =>
			{
				foreach(var tracked in cleanList)
				{
					tx.RemoveKey(tableName, tracked.GetRowKey());
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
