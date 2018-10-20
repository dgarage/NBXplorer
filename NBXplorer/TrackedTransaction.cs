using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using static NBXplorer.Repository;

namespace NBXplorer
{
	public class TrackedTransaction : TrackedTransactionBase
	{
		public TrackedTransaction(TrackedTransactionKey key, Transaction transaction, TransactionMiniMatch match) : base(key)
		{
			if (transaction == null)
				throw new ArgumentNullException(nameof(transaction));
			if (match == null)
				throw new ArgumentNullException(nameof(match));
			if (key.IsPruned)
			{
				throw new ArgumentException("The key should not be pruned", nameof(key));
			}
			Transaction = transaction;
			transaction.PrecomputeHash(false, true);
			TransactionMatch = match;
		}
		public Transaction Transaction
		{
			get;
		}

		public TransactionMiniMatch TransactionMatch
		{
			get;
		}

		public TxOut GetTxOut(uint n)
		{
			if (n >= Transaction.Outputs.Count)
				return null;
			return Transaction.Outputs[n];
		}

		public PrunedTrackedTransaction Prune()
		{
			var pruned = new PrunedTrackedTransaction(new TrackedTransactionKey(Key.TxId, Key.BlockHash, true));
			pruned.CopyFrom(this);
			return pruned;
		}
	}
}
