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

namespace ElementsExplorer
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
		public ExtPubKey PubKey
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
		public KeyInformation(ExtPubKey pubKey) : this(pubKey, null)
		{

		}
		public KeyInformation(ExtPubKey pubKey, KeyPath keyPath)
		{
			KeyPath = keyPath;
			RootKey = pubKey.ToBytes();
		}
		public byte[] RootKey
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
		public Repository(string directory, bool caching)
		{
			if(!Directory.Exists(directory))
				Directory.CreateDirectory(directory);
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

		public KeyInformation GetKeyInformation(ExtPubKey pubKey, Script script)
		{
			var info = GetKeyInformation(script);
			if(info == null || !pubKey.ToBytes().SequenceEqual(info.RootKey))
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
			var tableName = $"U-{Hashes.Hash160(info.RootKey).ToString()}";
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

				foreach(var trackedPath in TrackedPathes)
				{
					ExtPubKey pathPubKey = null;
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
						pathPubKey = pathPubKey ?? new ExtPubKey(info.RootKey).Derive(trackedPath);

						var scriptPubKey = pathPubKey.Derive((uint)highestUnused).PubKey.Hash.ScriptPubKey;
						InsertKeyInformation(tx, scriptPubKey, new KeyInformation()
						{
							KeyPath = trackedPath.Derive((uint)highestUnused),
							RootKey = info.RootKey
						});
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

		public string GetAssetName(uint256 assetId)
		{
			using(var tx = _Engine.GetTransaction())
			{
				var row = tx.Select<string, string>("AssetNameById", assetId.ToString());
				if(row == null || !row.Exists)
					return null;
				return row.Value;
			}
		}

		public enum SetNameResult
		{
			AssetNameAlreadyExist,
			AssetIdAlreadyClaimedAName,
			Success
		}
		public SetNameResult SetAssetName(NamedIssuance issuance)
		{
			using(var tx = _Engine.GetTransaction())
			{
				byte[] a;
				bool b;
				tx.Insert("AssetNameById", issuance.AssetId.ToString(), issuance.Name, out a, out b, true);
				if(b)
					return SetNameResult.AssetIdAlreadyClaimedAName;
				tx.Insert("AssetIdByName", issuance.Name, issuance.AssetId.ToString(), out a, out b, true);
				if(b)
					return SetNameResult.AssetNameAlreadyExist;
				tx.Commit();
				return SetNameResult.Success;
			}
		}



		public TrackedTransaction[] GetTransactions(BitcoinExtPubKey pubkey)
		{
			var tableName = $"T-{Hashes.Hash160(pubkey.ToBytes()).ToString()}";
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
			var groups = transactions.GroupBy(i => $"T-{Hashes.Hash160(i.PubKey.ToBytes()).ToString()}");

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

		public void CleanTransactions(ExtPubKey pubkey, List<TrackedTransaction> cleanList)
		{
			if(cleanList == null || cleanList.Count == 0)
				return;
			var tableName = $"T-{Hashes.Hash160(pubkey.ToBytes()).ToString()}";
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
