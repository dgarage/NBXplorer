using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using static NBXplorer.Repository;

namespace NBXplorer
{
	public class TrackedTransaction
	{
		public TrackedTransaction(TrackedTransactionKey key)
		{
			if (key == null)
				throw new ArgumentNullException(nameof(key));
			if (!key.IsPruned)
			{
				throw new ArgumentException("The key should be pruned", nameof(key));
			}
			Key = key;
		}
		public TrackedTransaction(TrackedTransactionKey key, Transaction transaction, TransactionMiniMatch match)
		{
			if (key == null)
				throw new ArgumentNullException(nameof(key));
			if (transaction == null)
				throw new ArgumentNullException(nameof(transaction));
			if (match == null)
				throw new ArgumentNullException(nameof(match));
			if (key.IsPruned)
			{
				throw new ArgumentException("The key should not be pruned", nameof(key));
			}
			Key = key;
			Transaction = transaction;
			transaction.PrecomputeHash(false, true);
			KnownKeyPathMapping.AddRange(match.Inputs.Concat(match.Outputs).Where(m => m.KeyPath != null));
			HashSet<Script> matchedScripts = match.Outputs.Select(o => o.ScriptPubKey).ToHashSet();

			for(int i = 0; i < transaction.Outputs.Count; i++)
			{
				var output = transaction.Outputs[i];
				if (matchedScripts.Contains(output.ScriptPubKey))
					ReceivedCoins.Add(new Coin(new OutPoint(key.TxId, i), output));
			}

			SpentOutpoints.AddRange(transaction.Inputs.Select(input => input.PrevOut));
		}

		public List<TransactionMiniKeyInformation> KnownKeyPathMapping { get; } = new List<TransactionMiniKeyInformation>();
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
			return new TrackedTransaction(new TrackedTransactionKey(Key.TxId, Key.BlockHash, true))
			{
				FirstSeen = FirstSeen,
				Inserted = Inserted
			};
		}
	}

	public class TrackedTransactionKey
	{
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
