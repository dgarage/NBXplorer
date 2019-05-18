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
		public AnnotatedTransaction(int? height, TrackedTransaction record, bool immature)
		{
			this.Record = record;
			this.Height = height;
			this.Immature = immature;
		}
		public int? Height
		{
			get;
		}
		public bool Immature { get; }
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

			int unconfCount = 0;
			foreach (var trackedTx in transactions)
			{
				AnnotatedTransaction annotatedTransaction = null;
				uint256 blockHash = null;
				if (trackedTx.BlockHash != null)
				{
					if (headerChain.TryGetHeight(trackedTx.BlockHash, out var height))
					{
						// Confirmed
						blockHash = trackedTx.BlockHash;
						var isMature = trackedTx.IsCoinBase ? headerChain.Height - height >= network.Consensus.CoinbaseMaturity : true;
						annotatedTransaction = new AnnotatedTransaction(height, trackedTx, !isMature);
					}
					else // Orphaned
					{
						blockHash = null;
						annotatedTransaction = new AnnotatedTransaction(null, trackedTx, false);
					}
				}
				else
				{
					// Unconfirmed
					blockHash = null;
					annotatedTransaction = new AnnotatedTransaction(null, trackedTx, false);
				}

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
					if (annotatedTransaction.Height is null)
						unconfCount++;
					_TxById.Add(trackedTx.TransactionHash, annotatedTransaction);
				}
			}

			Dictionary<OutPoint, uint256> spentBy = new Dictionary<OutPoint, uint256>(transactions.SelectMany(t => t.SpentOutpoints).Count());
			List<uint256> toRemove = new List<uint256>();
			foreach (var annotatedTransaction in _TxById.Values)
			{
				foreach (var spent in annotatedTransaction.Record.SpentOutpoints)
				{
					if (spentBy.TryGetValue(spent, out var conflictHash) &&
						_TxById.TryGetValue(conflictHash, out var conflicted))
					{
						if (ShouldReplace(annotatedTransaction, conflicted))
						{
							toRemove.Add(conflictHash);
							spentBy.Remove(spent);
							spentBy.Add(spent, annotatedTransaction.Record.BlockHash);

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
			}

			foreach (var e in toRemove)
				_TxById.Remove(e);

			var sortedTransactions = _TxById.Values.TopologicalSort();
			UTXOState state = new UTXOState();
			foreach (var tx in sortedTransactions.Where(s => !s.Immature))
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
			}
			AddRange(sortedTransactions);
			UTXOState = state;
			TrackedSource = trackedSource;
		}

		public UTXOState UTXOState { get; set; }

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

		public ICollection<AnnotatedTransaction> ReplacedTransactions
		{
			get; set;
		} = new List<AnnotatedTransaction>();

		public ICollection<AnnotatedTransaction> ConfirmedTransactions
		{
			get; set;
		} = new List<AnnotatedTransaction>();

		public ICollection<AnnotatedTransaction> UnconfirmedTransactions
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
