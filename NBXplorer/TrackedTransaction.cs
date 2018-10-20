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

		public PrunedTrackedTransaction Prune()
		{
			var pruned = new PrunedTrackedTransaction(new TrackedTransactionKey(Key.TxId, Key.BlockHash, true));
			pruned.CopyFrom(this);
			return pruned;
		}
	}
}
