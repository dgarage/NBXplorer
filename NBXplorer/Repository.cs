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

	public class KeyInformation
	{
		public KeyInformation()
		{

		}
		public KeyInformation(DerivationStrategyBase pubKey) : this(pubKey, null)
		{

		}
		public KeyInformation(DerivationStrategyBase pubKey, KeyPath keyPath)
		{
			KeyPath = keyPath;
			RootKey = pubKey;
		}
		public DerivationStrategyBase RootKey
		{
			get; set;
		}
		public KeyPath KeyPath
		{
			get; set;
		}
	}

	public class Repository : IDisposable
	{
		DBreezeEngine _Engine;
		Serializer Serializer;
		public Repository(Serializer serializer, string directory, bool caching)
		{
			if(serializer == null)
				throw new ArgumentNullException(nameof(serializer));
			if(!Directory.Exists(directory))
				Directory.CreateDirectory(directory);

			Serializer = serializer;
			_Engine = new DBreezeEngine(directory);

			Caching = caching;
			if(caching)
			{
				using(var tx = _Engine.GetTransaction())
				{
					tx.ValuesLazyLoadingIsOn = false;
					foreach(var existingRow in tx.SelectForward<string, byte[]>("KeysByScript"))
					{
						if(existingRow == null || !existingRow.Exists)
							continue;
						_Cache.TryAdd(new ScriptId(existingRow.Key), Serializer.ToObject<KeyInformation>(Unzip(existingRow.Value)));
					}
				}
			}
		}

		public BlockLocator GetIndexProgress()
		{
			using(var tx = _Engine.GetTransaction())
			{
				tx.ValuesLazyLoadingIsOn = false;
				var existingRow = tx.Select<string, byte[]>("IndexProgress", "");
				if(existingRow == null || !existingRow.Exists)
					return null;
				BlockLocator locator = new BlockLocator();
				locator.FromBytes(existingRow.Value);
				return locator;
			}
		}
		public void SetIndexProgress(BlockLocator locator)
		{
			using(var tx = _Engine.GetTransaction())
			{
				if(locator == null)
					tx.RemoveKey("IndexProgress", "");
				else
					tx.Insert("IndexProgress", "", locator.ToBytes());
				tx.Commit();
			}
		}

		public KeyPathInformation GetUnused(DerivationStrategyBase extPubKey, DerivationFeature derivationFeature, int n, bool reserve)
		{
			var tableName = $"U-{extPubKey.GetHash()}";
			var reservedTable = $"R-{extPubKey.GetHash()}";
			var readenTable = $"Read-{extPubKey.GetHash()}";

			var line = extPubKey.GetLineFor(derivationFeature);
			if(line == null)
				return null;
			List<KeyPath> possiblePaths = new List<KeyPath>();
			KeyPath path = null;
			using(var tx = _Engine.GetTransaction())
			{
				tx.ValuesLazyLoadingIsOn = false;
				tx.SynchronizeTables(reservedTable, readenTable, tableName);

				HashSet<KeyPath> reservedPaths = new HashSet<KeyPath>();
				foreach(var row in tx.SelectForward<string, bool>(reservedTable))
				{
					reservedPaths.Add(new KeyPath(row.Key));
				}

				HashSet<KeyPath> readenPaths = new HashSet<KeyPath>();

				if(reserve)
				{
					foreach(var row in tx.SelectForward<string, bool>(reservedTable))
					{
						readenPaths.Add(new KeyPath(row.Key));
					}
				}

				foreach(var row in tx.SelectForward<string, bool>(tableName))
				{
					var keyPath = new KeyPath(row.Key);
					if(!row.Value && keyPath.Parent == line.Path && !reservedPaths.Contains(keyPath))
					{
						if(!reserve || !readenPaths.Contains(keyPath))
							possiblePaths.Add(keyPath);
					}
				}
				path = possiblePaths.OrderBy(o => o.Indexes.Last()).Skip(n).FirstOrDefault();
				if(path != null)
				{
					if(reserve)
						tx.Insert(reservedTable, path.ToString(), true);
					tx.Insert(readenTable, path.ToString(), true);
					tx.Commit();
				}
			}

			if(path == null)
				return null;

			var derived = line.Derive(path.Indexes.Last());
			var keyInfo = new KeyPathInformation()
			{
				KeyPath = path,
				ScriptPubKey = derived.ScriptPubKey,
				Redeem = derived.Redeem,
				Address = derived.ScriptPubKey.GetDestinationAddress(Serializer.Network)
			};
			if(reserve)
			{
				MarkAsUsed(new KeyInformation(extPubKey, path));
			}
			return keyInfo;
		}

		public void SaveTransactions(Transaction[] transactions, uint256 blockHash)
		{
			transactions = transactions.Distinct().ToArray();
			if(transactions.Length == 0)
				return;
			using(var tx = _Engine.GetTransaction())
			{
				foreach(var btx in transactions)
				{
					tx.Insert("tx-" + btx.GetHash().ToString(), blockHash == null ? "0" : blockHash.ToString(), btx.ToBytes());
				}
				tx.Commit();
			}
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
			using(var tx = _Engine.GetTransaction())
			{
				foreach(var row in tx.SelectForward<string, byte[]>("tx-" + txid.ToString()))
				{
					SavedTransaction t = new SavedTransaction();
					if(row.Key.Length != 1)
						t.BlockHash = new uint256(row.Key);
					t.Transaction = new Transaction(row.Value);
					saved.Add(t);
				}
			}
			return saved.ToArray();
		}

		public KeyInformation GetKeyInformation(DerivationStrategyBase pubKey, Script script)
		{
			var info = GetKeyInformation(script);
			if(info == null || pubKey.GetHash() != info.RootKey.GetHash())
				return null;
			return info;
		}
		public KeyInformation GetKeyInformation(Script script)
		{
			if(Caching)
			{
				KeyInformation v;
				_Cache.TryGetValue(script.Hash, out v);
				return v;
			}
			using(var tx = _Engine.GetTransaction())
			{
				tx.ValuesLazyLoadingIsOn = false;
				var existingRow = tx.Select<string, byte[]>("KeysByScript", script.Hash.ToString());
				if(existingRow == null || !existingRow.Exists)
					return null;
				var keyInfo = Serializer.ToObject<KeyInformation>(Unzip(existingRow.Value));
				return keyInfo;
			}
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

		public const int MinGap = 20;
		readonly KeyPath[] TrackedPathes = new KeyPath[] { new KeyPath("0"), new KeyPath("1") };
		public void MarkAsUsed(KeyInformation info)
		{
			var tableName = $"U-{info.RootKey.GetHash()}";
			var highestUsedIndexes = new Dictionary<KeyPath, long>();
			var highestUnusedIndexes = new Dictionary<KeyPath, long>();
			using(var tx = _Engine.GetTransaction())
			{
				tx.ValuesLazyLoadingIsOn = false;
				if(info.KeyPath != null)
					tx.Insert(tableName, info.KeyPath.ToString(), true);

				foreach(var row in tx.SelectForward<string, bool>(tableName))
				{
					if(info.KeyPath == null)
						return; //Early exit, no need to create the first keys, it has already been done
					var highestIndexes = row.Value ? highestUsedIndexes : highestUnusedIndexes;
					KeyPath k = new KeyPath(row.Key);
					long highestKey;
					if(!highestIndexes.TryGetValue(k.Parent, out highestKey))
						highestKey = -1;
					highestKey = Math.Max(highestKey, k.Indexes.Last());
					highestIndexes.AddOrReplace(k.Parent, highestKey);
				}

				var derivationLines = info.RootKey.GetLines();
				foreach(var derivationLine in derivationLines)
				{
					var trackedPath = derivationLine.Path;
					long highestUnused;
					if(!highestUnusedIndexes.TryGetValue(trackedPath, out highestUnused))
						highestUnused = -1;

					long highestUsed;
					if(!highestUsedIndexes.TryGetValue(trackedPath, out highestUsed))
						highestUsed = -1;

					KeyPath highestUnusedPath = null;
					while(highestUnused - highestUsed < MinGap)
					{
						if(highestUnused == uint.MaxValue)
							break;
						highestUnused++;

						highestUnusedPath = trackedPath.Derive((uint)highestUnused);

						var derivation = derivationLine.Derive((uint)highestUnused);

						var keyInfo = new KeyInformation()
						{
							KeyPath = trackedPath.Derive((uint)highestUnused),
							RootKey = info.RootKey
						};
						InsertKeyInformation(tx, derivation.ScriptPubKey, keyInfo);
						byte[] inserted;
						bool existed;
						tx.Insert(tableName, keyInfo.KeyPath.ToString(), false, out inserted, out existed, dontUpdateIfExists: true);
					}

					if(highestUnusedPath != null)
					{
						byte[] inserted;
						bool existed;
						tx.Insert(tableName, highestUnusedPath.ToString(), false, out inserted, out existed, dontUpdateIfExists: true);
					}
				}
				tx.Commit();
			}
		}

		public TrackedTransaction[] GetTransactions(DerivationStrategyBase pubkey)
		{
			var tableName = $"T-{pubkey.GetHash()}";
			var result = new List<TrackedTransaction>();
			using(var tx = _Engine.GetTransaction())
			{
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
			}
			return result.ToArray();
		}


		public void InsertTransactions(InsertTransaction[] transactions)
		{
			if(transactions.Length == 0)
				return;
			var groups = transactions.GroupBy(i => $"T-{i.PubKey.GetHash()}");

			using(var tx = _Engine.GetTransaction())
			{
				foreach(var group in groups)
				{
					foreach(var value in group)
						tx.Insert(group.Key, value.TrackedTransaction.GetRowKey(), value.TrackedTransaction.Transaction.ToBytes());
				}
				tx.Commit();
			}
		}

		public void CleanTransactions(DerivationStrategyBase pubkey, List<TrackedTransaction> cleanList)
		{
			if(cleanList == null || cleanList.Count == 0)
				return;
			var tableName = $"T-{pubkey.GetHash()}";
			using(var tx = _Engine.GetTransaction())
			{
				foreach(var tracked in cleanList)
				{
					tx.RemoveKey(tableName, tracked.GetRowKey());
				}
				tx.Commit();
			}
		}

		public bool Caching
		{
			get;
			private set;
		}

		ConcurrentDictionary<ScriptId, KeyInformation> _Cache = new ConcurrentDictionary<ScriptId, KeyInformation>();

		private void InsertKeyInformation(DBreeze.Transactions.Transaction tx, Script scriptPubKey, KeyInformation info)
		{
			if(Caching)
				_Cache.TryAdd(scriptPubKey.Hash, info);
			tx.Insert("KeysByScript", scriptPubKey.Hash.ToString(), Zip(Serializer.ToString(info)));
		}

		public void Dispose()
		{
			_Engine.Dispose();
		}
	}
}
