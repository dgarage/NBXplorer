using NBitcoin;
using Microsoft.Extensions.Logging;
using NBXplorer.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer
{
	public enum AnnotatedTransactionType
	{
		Confirmed,
		Unconfirmed,
		Orphan
	}
	public class AnnotatedTransaction
	{
		public AnnotatedTransaction()
		{

		}
		public AnnotatedTransaction(TrackedTransaction tracked, ChainBase chain)
		{
			Record = tracked;
			if(tracked.BlockHash == null)
			{
				Type = AnnotatedTransactionType.Unconfirmed;
			}
			else
			{
				var block = chain.GetBlock(tracked.BlockHash);
				Type = block == null ? AnnotatedTransactionType.Orphan : AnnotatedTransactionType.Confirmed;
				Height = block?.Height;
			}
		}
		public AnnotatedTransactionType Type
		{
			get; set;
		}
		public int? Height
		{
			get;
			internal set;
		}
		public TrackedTransaction Record
		{
			get;
			internal set;
		}

		public override string ToString()
		{
			return Record?.Transaction?.GetHash()?.ToString() ?? "";
		}
	}

	public class AnnotatedTransactionCollection : List<AnnotatedTransaction>
	{
		Dictionary<uint256, DateTimeOffset> _FirstSeenByTxId = new Dictionary<uint256, DateTimeOffset>();
		public AnnotatedTransactionCollection(IEnumerable<AnnotatedTransaction> transactions) : base(transactions)
		{
			foreach(var tx in transactions)
			{
				var h = tx.Record.Transaction.GetHash();
				_TxById.Add(h, tx);
				UpdateFirstSeen(tx, h);
			}

			UnconfirmedTransactions = transactions
										.Where(tx => tx.Type == AnnotatedTransactionType.Unconfirmed || tx.Type == AnnotatedTransactionType.Orphan)
										.OrderByDescending(t => t.Record.Inserted) // OrderByDescending so that the last received is least likely to be conflicted
										.TopologicalSort().ToArray();
			ConfirmedTransactions = transactions
										.Where(tx => tx.Type == AnnotatedTransactionType.Confirmed)
										.TopologicalSort().ToArray();

			UTXOState state = new UTXOState();
			foreach(var confirmed in ConfirmedTransactions)
			{
				if(state.Apply(confirmed.Record.Transaction) == ApplyTransactionResult.Conflict)
				{
					Logs.Explorer.LogError("A conflict among confirmed transaction happened, this should be impossible");
					throw new InvalidOperationException("The impossible happened");
				}
			}

			var conflicted = new HashSet<AnnotatedTransaction>();
			foreach(var unconfirmed in UnconfirmedTransactions)
			{
				if(state.Apply(unconfirmed.Record.Transaction) == ApplyTransactionResult.Conflict)
				{
					conflicted.Add(unconfirmed);
				}
			}
			
			Conflicted = conflicted.ToArray();
			if(conflicted.Count != 0)
				UnconfirmedTransactions = UnconfirmedTransactions.Where(t => !conflicted.Contains(t)).ToArray();
		}

		private void UpdateFirstSeen(AnnotatedTransaction tx, uint256 h)
		{
			DateTimeOffset inserted;
			if(_FirstSeenByTxId.TryGetValue(h, out inserted))
			{
				if(inserted > tx.Record.Inserted)
					_FirstSeenByTxId[h] = inserted;
			}
			else
			{
				_FirstSeenByTxId.Add(h, tx.Record.Inserted);
			}
		}

		public DateTimeOffset GetFirstSeen(uint256 txId)
		{
			DateTimeOffset date;
			_FirstSeenByTxId.TryGetValue(txId, out date);
			return date;
		}

		MultiValueDictionary<uint256, AnnotatedTransaction> _TxById = new MultiValueDictionary<uint256, AnnotatedTransaction>();
		public IReadOnlyCollection<AnnotatedTransaction> GetByTxId(uint256 txId)
		{
			if(txId == null)
				throw new ArgumentNullException(nameof(txId));
			IReadOnlyCollection<AnnotatedTransaction> value;
			if(_TxById.TryGetValue(txId, out value))
				return value;
			return new List<AnnotatedTransaction>();
		}

		public AnnotatedTransaction[] ConfirmedTransactions
		{
			get; set;
		}
		public AnnotatedTransaction[] UnconfirmedTransactions
		{
			get; set;
		}

		public AnnotatedTransaction[] Conflicted
		{
			get; set;
		}
	}

}
