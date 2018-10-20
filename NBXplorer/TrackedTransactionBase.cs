using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer
{
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

	public abstract class TrackedTransactionBase
	{
		public TrackedTransactionBase(TrackedTransactionKey key)
		{
			if (key == null)
				throw new ArgumentNullException(nameof(key));
			Key = key;
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

		public void CopyFrom(TrackedTransactionBase trackedTransaction)
		{
			this.FirstSeen = FirstSeen;
			this.Inserted = Inserted;
		}
	}
}
