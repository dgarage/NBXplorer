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
		public AnnotatedTransaction(long? height, TrackedTransaction record, bool isMature)
		{
			this.Record = record;
			this.Height = height;
			this.IsMature = isMature;
		}
		public long? Height
		{
			get;
		}
		public bool IsMature { get; }
		public TrackedTransaction Record
		{
			get;
		}
		public uint256 ReplacedBy { get; set; }
		public bool Replaceable { get; set; }
		public uint256 Replacing { get; set; }

		public override string ToString()
		{
			return Record.TransactionHash.ToString();
		}
	}

	public class AnnotatedTransactionCollection : List<AnnotatedTransaction>
	{
		public AnnotatedTransactionCollection(ICollection<TrackedTransaction> transactions, Models.TrackedSource trackedSource, Network network) : base(transactions.Count)
		{
			_TxById = new Dictionary<uint256, AnnotatedTransaction>(transactions.Count);
			ConfirmedTransactions = new List<AnnotatedTransaction>(transactions.Count);
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
				long? txHeight = trackedTx.BlockHeight;
				bool isMature = !trackedTx.Immature;

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
			foreach (var annotatedTransaction in _TxById.Values.Where(r => r.Height is long))
			{
				foreach (var spent in annotatedTransaction.Record.SpentOutpoints)
				{
					// No way to have double spent in confirmed transactions
					try
					{
						spentBy.Add(spent, annotatedTransaction.Record.TransactionHash);
					}
					catch
					{
						throw;
					}
				}
			}

			var unconfs = _TxById.Where(r => r.Value.Height is null)
							.ToDictionary(kv => kv.Key, kv => kv.Value);
			var replaced = new Dictionary<uint256, AnnotatedTransaction>();
			removeConflicts:
			HashSet<uint256> toRemove = new HashSet<uint256>();
			foreach (var annotatedTransaction in unconfs.Values)
			{
				foreach (var spent in annotatedTransaction.Record.SpentOutpoints)
				{
					// All children of a replaced transaction should be replaced
					if (replaced.TryGetValue(spent.Hash, out var parent) && parent.ReplacedBy is uint256)
					{
						annotatedTransaction.Replaceable = false;
						annotatedTransaction.ReplacedBy = parent.ReplacedBy;
						replaced.TryAdd(annotatedTransaction.Record.TransactionHash, annotatedTransaction);
						toRemove.Add(annotatedTransaction.Record.TransactionHash);
						goto nextTransaction;
					}

					// If there is a conflict, let's see who get replaced
					if (spentBy.TryGetValue(spent, out var conflictHash) &&
						_TxById.TryGetValue(conflictHash, out var conflicted))
					{
						// Conflict with one-self... not a conflict.
						if (conflicted == annotatedTransaction)
							goto nextTransaction;
						// We know the conflict is already removed, so this transaction replace it
						if (toRemove.Contains(conflictHash))
						{
							spentBy.Remove(spent);
							spentBy.Add(spent, annotatedTransaction.Record.TransactionHash);
						}
						else
						{
							AnnotatedTransaction toKeep = null, toReplace = null;
							var shouldReplace = ShouldReplace(annotatedTransaction, conflicted);
							if (shouldReplace)
							{
								toReplace = conflicted;
								toKeep = annotatedTransaction;
							}
							else
							{
								toReplace = annotatedTransaction;
								toKeep = conflicted;
							}
							toRemove.Add(toReplace.Record.TransactionHash);

							if (toKeep == annotatedTransaction)
							{
								spentBy.Remove(spent);
								spentBy.Add(spent, annotatedTransaction.Record.TransactionHash);
							}

							if (toKeep.Height is null && toReplace.Height is null)
							{
								toReplace.ReplacedBy = toKeep.Record.TransactionHash;
								toReplace.Replaceable = false;
								toKeep.Replacing = toReplace.Record.TransactionHash;
								replaced.TryAdd(toReplace.Record.TransactionHash, toReplace);
							}
							else
							{
								CleanupTransactions.Add(toReplace);
							}
						}
					}
					else
					{
						spentBy.Remove(spent);
						spentBy.Add(spent, annotatedTransaction.Record.TransactionHash);
					}
				}
			nextTransaction:;
			}

			foreach (var e in toRemove)
			{
				_TxById.Remove(e);
				unconfs.Remove(e);
			}
			if (toRemove.Count != 0)
				goto removeConflicts;

			var sortedTransactions = _TxById.Values.TopologicalSort();
			ReplacedTransactions = replaced.Values.TopologicalSort().ToList();
			UTXOState state = new UTXOState(sortedTransactions.Count);
			foreach (var tx in sortedTransactions.Where(s => s.IsMature && s.Height is long))
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
				if (tx.Height is long)
				{
					ConfirmedTransactions.Add(tx);
					if (!tx.IsMature)
						ImmatureTransactions.Add(tx);
				}
				else
				{
					UnconfirmedTransactions.Add(tx);
					// A transaction is replaceable if it is RBF and we control all inputs
					tx.Replaceable = tx.Record.Transaction?.RBF is true &&
									tx.Record.Transaction?.Inputs.Count() is int txInputCount &&
									tx.Record.SpentOutpoints.Count == txInputCount;
					if (tx.Replaceable)
					{
						// Parents of a transaction should not be replaceable (technically can in the protocol)
						// but we don't want user cancelling a chain of transaction
						foreach (var parentOutpoint in tx.Record.SpentOutpoints)
						{
							if (_TxById.TryGetValue(parentOutpoint.Hash, out var parent) && parent.Height is null)
							{
								parent.Replaceable = false;
							}
						}
					}
				}
				this.Add(tx);
			}
			UnconfirmedState = state;
			TrackedSource = trackedSource;
		}

		public UTXOState ConfirmedState { get; set; }
		public UTXOState UnconfirmedState { get; set; }

		private static bool ShouldReplace(AnnotatedTransaction annotatedTransaction, AnnotatedTransaction conflicted)
		{
			if (annotatedTransaction.Height is null)
			{
				if (conflicted.Height is null)
				{
					return annotatedTransaction.Record.FirstSeen > conflicted.Record.FirstSeen; // The is a replaced tx, we want the youngest
				}
				else
				{
					return false;
				}
			}
			else
			{
				if (conflicted.Height is null)
				{
					return true;
				}
				else
				{
					if (annotatedTransaction.Record.Key.TxId == conflicted.Record.Key.TxId && 
						annotatedTransaction.Height == conflicted.Height)
					{
						return !annotatedTransaction.Record.Key.IsPruned;
					}
					return annotatedTransaction.Height < conflicted.Height; // The most buried block win (should never happen though)
				}
			}
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
			get;
		} = new List<AnnotatedTransaction>();

		public List<AnnotatedTransaction> ConfirmedTransactions
		{
			get;
		}

		public List<AnnotatedTransaction> ImmatureTransactions
		{
			get;
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
