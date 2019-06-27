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
		class AnnotatedTransactionComparer : IComparer<AnnotatedTransaction>
		{
			AnnotatedTransactionComparer()
			{

			}
			private static readonly AnnotatedTransactionComparer _Instance = new AnnotatedTransactionComparer();
			public static AnnotatedTransactionComparer Instance
			{
				get
				{
					return _Instance;
				}
			}
			public int Compare(AnnotatedTransaction a, AnnotatedTransaction b)
			{
				var txIdCompare = a.Record.TransactionHash < b.Record.TransactionHash ? -1 :
								  a.Record.TransactionHash > b.Record.TransactionHash ? 1 : 0;
				var seenCompare = (a.Record.FirstSeen < b.Record.FirstSeen ? -1 :
								a.Record.FirstSeen > b.Record.FirstSeen ? 1 : txIdCompare);
				if (a.Height is int ah)
				{
					// Both confirmed, tie on height then firstSeen
					if (b.Height is int bh)
					{
						var heightCompare = (ah < bh ? -1 :
							   ah > bh ? 1 : txIdCompare);
						return ah == bh ?
							   // same height? use firstSeen on firstSeen
							   seenCompare :
							   // else tie on the height
							   heightCompare;
					}
					else
					{
						return -1;
					}
				}
				else if (b.Height is int bh)
				{
					return 1;
				}
				// Both unconfirmed, tie on firstSeen
				else
				{
					return seenCompare;
				}
			}
		}
		public static ICollection<AnnotatedTransaction> TopologicalSort(this ICollection<AnnotatedTransaction> transactions)
		{
			return transactions.TopologicalSort(
				dependsOn: t => t.Record.SpentOutpoints.Select(o => o.Hash),
				getKey: t => t.Record.TransactionHash,
				getValue: t => t,
				solveTies: AnnotatedTransactionComparer.Instance);
		}
		public static List<T> TopologicalSort<T>(this ICollection<T> nodes, Func<T, IEnumerable<T>> dependsOn)
		{
			return nodes.TopologicalSort(dependsOn, k => k, k => k);
		}

		public static List<T> TopologicalSort<T, TDepend>(this ICollection<T> nodes, Func<T, IEnumerable<TDepend>> dependsOn, Func<T, TDepend> getKey)
		{
			return nodes.TopologicalSort(dependsOn, getKey, o => o);
		}

		public static List<TValue> TopologicalSort<T, TDepend, TValue>(this ICollection<T> nodes,
												Func<T, IEnumerable<TDepend>> dependsOn,
												Func<T, TDepend> getKey,
												Func<T, TValue> getValue,
												IComparer<T> solveTies = null)
		{
			if (nodes.Count == 0)
				return new List<TValue>();
			if (getKey == null)
				throw new ArgumentNullException(nameof(getKey));
			if (getValue == null)
				throw new ArgumentNullException(nameof(getValue));
			solveTies = solveTies ?? Comparer<T>.Default;
			List<TValue> result = new List<TValue>(nodes.Count);
			HashSet<TDepend> allKeys = new HashSet<TDepend>(nodes.Count);
			var noDependencies = new SortedDictionary<T, HashSet<TDepend>>(solveTies);

			foreach (var node in nodes)
				allKeys.Add(getKey(node));
			var dependenciesByValues = nodes.ToDictionary(node => node,
										   node => new HashSet<TDepend>(dependsOn(node).Where(n => allKeys.Contains(n))));
			foreach (var e in dependenciesByValues.Where(x => x.Value.Count == 0))
			{
				noDependencies.Add(e.Key, e.Value);
			}
			if (noDependencies.Count == 0)
			{
				throw new InvalidOperationException("Impossible to topologically sort a cyclic graph");
			}
			while (noDependencies.Count > 0)
			{
				var nodep = noDependencies.First();
				noDependencies.Remove(nodep.Key);
				dependenciesByValues.Remove(nodep.Key);

				var elemKey = getKey(nodep.Key);
				result.Add(getValue(nodep.Key));
				foreach (var selem in dependenciesByValues)
				{
					if (selem.Value.Remove(elemKey) && selem.Value.Count == 0)
						noDependencies.Add(selem.Key, selem.Value);
				}
			}
			if (dependenciesByValues.Count != 0)
			{
				throw new InvalidOperationException("Impossible to topologically sort a cyclic graph");
			}
			return result;
		}

		public static TransactionResult ToTransactionResult(SlimChain chain, Repository.SavedTransaction[] result)
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

			return new TransactionResult() { Confirmations = conf, BlockId = confBlock?.Hash, Transaction = oldest.Transaction, TransactionHash = oldest.Transaction.GetHash(), Height = confBlock?.Height, Timestamp = oldest.Timestamp };
		}
	}
}
