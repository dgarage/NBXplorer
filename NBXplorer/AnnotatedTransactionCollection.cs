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
		public AnnotatedTransaction(TrackedTransaction tracked, SlimChain chain)
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
			return Record?.TransactionHash.ToString() ?? "";
		}
	}

	public class AnnotatedTransactionCollection : List<AnnotatedTransaction>
	{
		public AnnotatedTransactionCollection(IEnumerable<AnnotatedTransaction> transactions) : base(transactions)
		{
			foreach(var tx in transactions)
			{
				_TxById.Add(tx.Record.TransactionHash, tx);
				foreach(var keyPathInfo in tx.Record.TransactionMatch.Inputs.Concat(tx.Record.TransactionMatch.Outputs))
				{
					if(keyPathInfo.KeyPath != null)
						_KeyPaths.TryAdd(keyPathInfo.ScriptPubKey, keyPathInfo.KeyPath);
				}
			}


			UTXOState state = new UTXOState();
			foreach(var confirmed in transactions
										.Where(tx => tx.Type == AnnotatedTransactionType.Confirmed)
										.TopologicalSort())
			{
				if(state.Apply(confirmed.Record) == ApplyTransactionResult.Conflict
					|| !ConfirmedTransactions.TryAdd(confirmed.Record.TransactionHash, confirmed))
				{
					Logs.Explorer.LogError("A conflict among confirmed transaction happened, this should be impossible");
					throw new InvalidOperationException("The impossible happened");
				}
			}

			foreach(var unconfirmed in transactions
										.Where(tx => tx.Type == AnnotatedTransactionType.Unconfirmed || tx.Type == AnnotatedTransactionType.Orphan)
										.OrderByDescending(t => t.Record.Inserted) // OrderByDescending so that the last received is least likely to be conflicted
										.TopologicalSort())
			{
				var hash = unconfirmed.Record.TransactionHash;
				if(ConfirmedTransactions.ContainsKey(hash))
				{
					DuplicatedTransactions.Add(unconfirmed);
				}
				else
				{
					if(state.Apply(unconfirmed.Record) == ApplyTransactionResult.Conflict)
					{
						ReplacedTransactions.TryAdd(hash, unconfirmed);
					}
					else
					{
						if(!UnconfirmedTransactions.TryAdd(hash, unconfirmed))
						{
							throw new InvalidOperationException("The impossible happened (!UnconfirmedTransactions.TryAdd(hash, unconfirmed))");
						}
					}
				}
			}
		}

		public TxOut GetUTXO(OutPoint outpoint)
		{
			if(_TxById.TryGetValue(outpoint.Hash, out var txs))
			{
				return txs.Select(t => t.Record.GetTxOut(outpoint.N)).FirstOrDefault();
			}
			return null;
		}

		Dictionary<Script, KeyPath> _KeyPaths = new Dictionary<Script, KeyPath>();
		public KeyPath GetKeyPath(Script scriptPubkey)
		{
			return _KeyPaths.TryGet(scriptPubkey);
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

		public Dictionary<uint256, AnnotatedTransaction> ReplacedTransactions
		{
			get; set;
		} = new Dictionary<uint256, AnnotatedTransaction>();

		public Dictionary<uint256, AnnotatedTransaction> ConfirmedTransactions
		{
			get; set;
		} = new Dictionary<uint256, AnnotatedTransaction>();

		public Dictionary<uint256, AnnotatedTransaction> UnconfirmedTransactions
		{
			get; set;
		} = new Dictionary<uint256, AnnotatedTransaction>();

		public List<AnnotatedTransaction> DuplicatedTransactions
		{
			get; set;
		} = new List<AnnotatedTransaction>();
	}

}
