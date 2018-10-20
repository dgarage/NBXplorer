using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using static NBXplorer.Repository;

namespace NBXplorer
{
	public class PrunedTrackedTransaction : TrackedTransactionBase
	{
		public PrunedTrackedTransaction(TrackedTransactionKey key) : base(key)
		{
			if (!key.IsPruned)
			{
				throw new ArgumentException("The key should be pruned", nameof(key));
			}
		}

		public TrackedTransaction Unprune(Transaction transaction, TransactionMiniMatch match)
		{
			var unpruned = new TrackedTransaction(new TrackedTransactionKey(Key.TxId, Key.BlockHash, true), transaction, match);
			unpruned.CopyFrom(this);
			return unpruned;
		}
	}
}
