using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using static NBXplorer.Repository;

namespace NBXplorer{
	public class TrackedTransaction
	{
		public TrackedTransaction(TrackedTransactionKey key, TrackedSource trackedSource) : this(key, trackedSource, null as Coin[], null as Dictionary<Script,KeyPath>)
		{

		}
		public TrackedTransaction(TrackedTransactionKey key, TrackedSource trackedSource, IEnumerable<Coin> receivedCoins, IEnumerable<KeyPathInformation> knownScriptMapping)
			: this(key, trackedSource, receivedCoins, ToDictionary(knownScriptMapping))
		{

		}

		public TrackedSource TrackedSource { get; }

		private static Dictionary<Script, KeyPath> ToDictionary(IEnumerable<KeyPathInformation> knownScriptMapping)
		{
			if (knownScriptMapping == null)
				return null;
			var result = new Dictionary<Script, KeyPath>();
			foreach (var keypathInfo in knownScriptMapping)
			{
				result.TryAdd(keypathInfo.ScriptPubKey, keypathInfo.KeyPath);
			}
			return result;
		}
		public TrackedTransaction(TrackedTransactionKey key, TrackedSource trackedSource, IEnumerable<Coin> receivedCoins, Dictionary<Script, KeyPath> knownScriptMapping)
		{
			if (key == null)
				throw new ArgumentNullException(nameof(key));
			if (!key.IsPruned)
			{
				throw new ArgumentException("The key should be pruned", nameof(key));
			}
			if (trackedSource == null)
				throw new ArgumentNullException(nameof(trackedSource));
			TrackedSource = trackedSource;
			Key = key;
			if(knownScriptMapping != null)
				KnownKeyPathMapping = knownScriptMapping;
			if (receivedCoins != null)
				ReceivedCoins.AddRange(receivedCoins);
		}
		public TrackedTransaction(TrackedTransactionKey key, TrackedSource trackedSource, Transaction transaction, Dictionary<Script, KeyPath> knownScriptMapping)
		{
			if (key == null)
				throw new ArgumentNullException(nameof(key));
			if (transaction == null)
				throw new ArgumentNullException(nameof(transaction));
			if (knownScriptMapping == null)
				throw new ArgumentNullException(nameof(knownScriptMapping));
			if (trackedSource == null)
				throw new ArgumentNullException(nameof(trackedSource));
			if (key.IsPruned)
			{
				throw new ArgumentException("The key should not be pruned", nameof(key));			}
			Key = key;
			TrackedSource = trackedSource;
			Transaction = transaction;
			transaction.PrecomputeHash(false, true);
			KnownKeyPathMapping = knownScriptMapping;

			KnownKeyPathMappingUpdated();
		}

		internal void KnownKeyPathMappingUpdated()
		{
			if (Transaction == null)
				return;
			var scriptPubKey = (TrackedSource as IDestination)?.ScriptPubKey;
			for (int i = 0; i < Transaction.Outputs.Count; i++)
			{
				var output = Transaction.Outputs[i];
				if (KnownKeyPathMapping.ContainsKey(output.ScriptPubKey) || scriptPubKey == output.ScriptPubKey)
					ReceivedCoins.Add(new Coin(new OutPoint(Key.TxId, i), output));
			}
			SpentOutpoints.AddRange(Transaction.Inputs.Select(input => input.PrevOut));
		}

		public Dictionary<Script, KeyPath> KnownKeyPathMapping { get; } = new Dictionary<Script, KeyPath>();
		public List<Coin> ReceivedCoins { get; } = new List<Coin>();
		public List<OutPoint> SpentOutpoints { get; } = new List<OutPoint>();

		public Transaction Transaction
		{
			get;
		}

		public TrackedTransactionKey Key { get; }
		public uint256 BlockHash => Key.BlockHash;
		public uint256 TransactionHash => Key.TxId;

		public DateTimeOffset Inserted
		{
			get; set;
		}

		public DateTimeOffset FirstSeen
		{
			get; set;
		}


		public TrackedTransaction Prune()
		{
			// Pruning transactions, coins and known keys
			return new TrackedTransaction(new TrackedTransactionKey(Key.TxId, Key.BlockHash, true), TrackedSource)
			{
				FirstSeen = FirstSeen,
				Inserted = Inserted
			};
		}

		internal IEnumerable<KeyPathInformation> GetKeyPaths(DerivationStrategyBase derivationStrategy)
		{
			return KnownKeyPathMapping.Values.Select(v => new KeyPathInformation(v, derivationStrategy));
		}

		public IEnumerable<MatchedOutput> GetReceivedOutputs()
		{
			return this.ReceivedCoins
							.Select(o => (Index: (int)o.Outpoint.N,
												   Output: o,
												   KeyPath: KnownKeyPathMapping.TryGet(o.ScriptPubKey)))
							.Where(o => o.KeyPath != null || o.Output.ScriptPubKey == (TrackedSource as IDestination)?.ScriptPubKey)
							.Select(o => new MatchedOutput()
							{
								Index = o.Index,
								Value = o.Output.Amount,
								KeyPath = o.KeyPath,
								ScriptPubKey = o.Output.ScriptPubKey
							});
		}
	}

	public class TrackedTransactionKey	{
		public uint256 TxId { get; }
		public uint256 BlockHash { get; }

		public static TrackedTransactionKey Parse(string str)
		{
			str = str.Split('-').Last();
			var splitted = str.Split(':');

			var txStr = splitted[0];
			uint256 txHash = new uint256(txStr);

			var blockHashStr = splitted[1];
			uint256 blockHash = null;
			if (blockHashStr.Length != 0)
				blockHash = new uint256(blockHashStr);

			var pruned = false;
			if (splitted.Length > 2)
			{
				pruned = splitted[2] == "P";
			}
			return new TrackedTransactionKey(txHash, blockHash, pruned);
		}

		public TrackedTransactionKey(uint256 txId, uint256 blockHash, bool pruned)
		{
			TxId = txId;
			BlockHash = blockHash;
			IsPruned = pruned;
		}

		public bool IsPruned { get; set; }

		public override bool Equals(object obj)
		{
			TrackedTransactionKey item = obj as TrackedTransactionKey;
			if (item == null)
				return false;
			return ToString().Equals(item.ToString());
		}
		public static bool operator ==(TrackedTransactionKey a, TrackedTransactionKey b)
		{
			if (System.Object.ReferenceEquals(a, b))
				return true;
			if (((object)a == null) || ((object)b == null))
				return false;
			return a.ToString() == b.ToString();
		}

		public static bool operator !=(TrackedTransactionKey a, TrackedTransactionKey b)
		{
			return !(a == b);
		}

		public override int GetHashCode()
		{
			return ToString().GetHashCode();
		}

		public override string ToString()
		{
			var prunedSuffix = IsPruned ? ":P" : string.Empty;
			return $"{TxId}:{BlockHash}{prunedSuffix}";
		}
	}
}
