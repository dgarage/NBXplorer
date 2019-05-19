using NBitcoin;
using Microsoft.Extensions.Logging;
using NBXplorer.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBXplorer.Models;

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
		public AnnotatedTransaction(int? height, TrackedTransaction record, bool isMature)
		{
			this.Record = record;
			this.Height = height;
			this.IsMature = isMature;
		}
		public int? Height
		{
			get;
		}
		public bool IsMature { get; }
		public TrackedTransaction Record
		{
			get;
		}

		public override string ToString()
		{
			return Record.TransactionHash.ToString();
		}
	}

	public class AnnotatedTransactionCollection : List<AnnotatedTransaction>
	{
		public AnnotatedTransactionCollection(ICollection<TrackedTransaction> transactions, Models.TrackedSource trackedSource, SlimChain headerChain, Network network) : base(transactions.Count)
		{
			_TxById = new Dictionary<uint256, AnnotatedTransaction>(transactions.Count);
			foreach (var tx in transactions)
			{
				foreach (var keyPathInfo in tx.KnownKeyPathMapping)
				{
					_KeyPaths.TryAdd(keyPathInfo.Key, keyPathInfo.Value);
				}
			}

			// Let's remove the dups and let's get the current height of the transactions
			foreach (var trackedTx in transactions)
			{
				int? txHeight = null;
				bool isMature = true;

				if (trackedTx.BlockHash != null && headerChain.TryGetHeight(trackedTx.BlockHash, out var height))
				{
					txHeight = height;
					isMature = trackedTx.IsCoinBase ? headerChain.Height - height >= network.Consensus.CoinbaseMaturity : true;
				}

				AnnotatedTransaction annotatedTransaction = new AnnotatedTransaction(txHeight, trackedTx, isMature);
				if (_TxById.TryGetValue(trackedTx.TransactionHash, out var conflicted))
				{
					if (ShouldReplace(annotatedTransaction, conflicted))
					{
						CleanupTransactions.Add(conflicted);
						_TxById.Remove(trackedTx.TransactionHash);
						_TxById.Add(trackedTx.TransactionHash, annotatedTransaction);
					}
					else
					{
						CleanupTransactions.Add(annotatedTransaction);
					}
				}
				else
				{
					_TxById.Add(trackedTx.TransactionHash, annotatedTransaction);
				}
			}

			// Let's resolve the double spents
			Dictionary<OutPoint, uint256> spentBy = new Dictionary<OutPoint, uint256>(transactions.SelectMany(t => t.SpentOutpoints).Count());
			foreach (var annotatedTransaction in _TxById.Values.Where(r => r.Height is int))
			{
				foreach (var spent in annotatedTransaction.Record.SpentOutpoints)
				{
					// No way to have double spent in confirmed transactions
					spentBy.Add(spent, annotatedTransaction.Record.TransactionHash);
				}
			}

		removeConflicts:
			HashSet<uint256> toRemove = new HashSet<uint256>();
			foreach (var annotatedTransaction in _TxById.Values.Where(r => r.Height is null))
			{
				foreach (var spent in annotatedTransaction.Record.SpentOutpoints)
				{
					if (spentBy.TryGetValue(spent, out var conflictHash) &&
						_TxById.TryGetValue(conflictHash, out var conflicted))
					{
						if (conflicted == annotatedTransaction)
							goto nextTransaction;
						if (toRemove.Contains(conflictHash))
						{
							spentBy.Remove(spent);
							spentBy.Add(spent, annotatedTransaction.Record.TransactionHash);
						}
						else if (ShouldReplace(annotatedTransaction, conflicted))
						{
							toRemove.Add(conflictHash);
							spentBy.Remove(spent);
							spentBy.Add(spent, annotatedTransaction.Record.TransactionHash);

							if (conflicted.Height is null && annotatedTransaction.Height is null)
							{
								ReplacedTransactions.Add(conflicted);
							}
							else
							{
								CleanupTransactions.Add(conflicted);
							}
						}
						else
						{
							toRemove.Add(annotatedTransaction.Record.TransactionHash);
							if (conflicted.Height is null && annotatedTransaction.Height is null)
							{
								ReplacedTransactions.Add(annotatedTransaction);
							}
							else
							{
								CleanupTransactions.Add(annotatedTransaction);
							}
						}
					}
					else
					{
						spentBy.Add(spent, annotatedTransaction.Record.TransactionHash);
					}
				}
			nextTransaction:;
			}

			foreach (var e in toRemove)
				_TxById.Remove(e);
			if (toRemove.Count != 0)
				goto removeConflicts;

			var sortedTransactions = _TxById.Values.TopologicalSort();
			UTXOState state = new UTXOState();
			foreach (var tx in sortedTransactions.Where(s => s.IsMature && s.Height is int))
			{
				if (state.Apply(tx.Record) == ApplyTransactionResult.Conflict)
				{
					throw new InvalidOperationException("The impossible happened");
				}
			}
			ConfirmedState = state.Snapshot();
			foreach (var tx in sortedTransactions.Where(s => s.Height is null))
			{
				if (state.Apply(tx.Record) == ApplyTransactionResult.Conflict)
				{
					throw new InvalidOperationException("The impossible happened");
				}
			}
			foreach (var tx in sortedTransactions)
			{
				if (tx.Height is int)
					ConfirmedTransactions.Add(tx);
				else
					UnconfirmedTransactions.Add(tx);
				this.Add(tx);
			}
			UnconfirmedState = state;
			TrackedSource = trackedSource;
		}

		public UTXOState ConfirmedState { get; set; }
		public UTXOState UnconfirmedState { get; set; }

		private static bool ShouldReplace(AnnotatedTransaction annotatedTransaction, AnnotatedTransaction conflicted)
		{
			if (annotatedTransaction.Height is int &&
				conflicted.Height is null)
			{
				return true;
			}
			else if (annotatedTransaction.Height is null &&
					 conflicted.Height is null &&
					 annotatedTransaction.Record.Inserted > conflicted.Record.Inserted)
			{
				return true;
			}
			else if (annotatedTransaction.Height is int &&
					 conflicted.Height is int &&
					 conflicted.Record.Key.IsPruned)
			{
				return true;
			}

			return false;
		}

		public MatchedOutput GetUTXO(OutPoint outpoint)
		{
			if (_TxById.TryGetValue(outpoint.Hash, out var tx))
			{
				return tx.Record.GetReceivedOutputs().Where(c => c.Index == outpoint.N).FirstOrDefault();
			}
			return null;
		}

		Dictionary<Script, KeyPath> _KeyPaths = new Dictionary<Script, KeyPath>();
		public KeyPath GetKeyPath(Script scriptPubkey)
		{
			return _KeyPaths.TryGet(scriptPubkey);
		}

		Dictionary<uint256, AnnotatedTransaction> _TxById = new Dictionary<uint256, AnnotatedTransaction>();
		public AnnotatedTransaction GetByTxId(uint256 txId)
		{
			if (txId == null)
				throw new ArgumentNullException(nameof(txId));
			_TxById.TryGetValue(txId, out var value);
			return value;
		}

		public List<AnnotatedTransaction> ReplacedTransactions
		{
			get; set;
		} = new List<AnnotatedTransaction>();

		public List<AnnotatedTransaction> ConfirmedTransactions
		{
			get; set;
		} = new List<AnnotatedTransaction>();

		public List<AnnotatedTransaction> UnconfirmedTransactions
		{
			get; set;
		} = new List<AnnotatedTransaction>();

		public List<AnnotatedTransaction> CleanupTransactions
		{
			get; set;
		} = new List<AnnotatedTransaction>();
		public Models.TrackedSource TrackedSource { get; }
	}

}
