using NBitcoin;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NBXplorer
{
	public static class Utils
	{
		public static IEnumerable<Transaction> TopologicalSort(this IEnumerable<Transaction> transactions)
		{
			return transactions
				.Select(t => t.AsAnnotatedTransaction())
				.TopologicalSort()
				.Select(t => t.Record.Transaction);
		}

		static AnnotatedTransaction AsAnnotatedTransaction(this Transaction tx)
		{
			return new AnnotatedTransaction() { Record = new TrackedTransaction(new TrackedTransactionKey(tx.GetHash(), null, false), tx, new Repository.TransactionMiniMatch()) };
		}
		
		public static IEnumerable<AnnotatedTransaction> TopologicalSort(this IEnumerable<AnnotatedTransaction> transactions)
		{
			transactions = transactions.ToList(); // Buffer
			return transactions.TopologicalSort<AnnotatedTransaction>(DependsOn(transactions));
		}

		static Func<AnnotatedTransaction, IEnumerable<AnnotatedTransaction>> DependsOn(IEnumerable<AnnotatedTransaction> transactions)
		{
			return t =>
			{
				HashSet<uint256> spent = new HashSet<uint256>(t.Record.Transaction.Inputs.Select(txin => txin.PrevOut.Hash));
				return transactions.Where(u => spent.Contains(u.Record.TransactionHash) ||  //Depends on parent transaction
												(u.Height.HasValue && t.Height.HasValue && u.Height.Value < t.Height.Value) ); //Depends on earlier transaction
			};
		}

		public static IEnumerable<T> TopologicalSort<T>(this IEnumerable<T> nodes,
												Func<T, IEnumerable<T>> dependsOn)
		{
			List<T> result = new List<T>();
			var elems = nodes.ToDictionary(node => node,
										   node => new HashSet<T>(dependsOn(node)));
			while(elems.Count > 0)
			{
				var elem = elems.FirstOrDefault(x => x.Value.Count == 0);
				if(elem.Key == null)
				{
					//cycle detected can't order
					return nodes;
				}
				elems.Remove(elem.Key);
				foreach(var selem in elems)
				{
					selem.Value.Remove(elem.Key);
				}
				result.Add(elem.Key);
			}
			return result;
		}

		public static TransactionResult ToTransactionResult(bool includeTransaction, SlimChain chain, Repository.SavedTransaction[] result)
		{
			var noDate = NBitcoin.Utils.UnixTimeToDateTime(0);
			var oldest = result
							.Where(o => o.Timestamp != noDate)
							.OrderBy(o => o.Timestamp).FirstOrDefault() ?? result.First();

			var confBlock = result
						.Where(r => r.BlockHash != null)
						.Select(r => chain.GetBlock(r.BlockHash))
						.Where(r => r != null)
						.FirstOrDefault();

			var conf = confBlock == null ? 0 : chain.Height - confBlock.Height + 1;

			return new TransactionResult() { Confirmations = conf, BlockId = confBlock?.Hash, Transaction = includeTransaction ? oldest.Transaction : null, Height = confBlock?.Height, Timestamp = oldest.Timestamp };
		}
	}
}
