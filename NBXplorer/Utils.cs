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
		public static ICollection<AnnotatedTransaction> TopologicalSort(this ICollection<AnnotatedTransaction> transactions)
		{
			return transactions.TopologicalSort(
				dependsOn: t => t.Record.SpentOutpoints.Select(o => o.Hash),
				getKey: t => t.Record.TransactionHash,
				getValue: t => t,
				solveTies: (a, b) =>
				{
					if (a.Height is int ah)
					{
						// Both confirmed, tie on height then firstSeen
						if (b.Height is int bh)
						{
							return ah == bh ?
								   // same height? tire on firstSeen
								   (a.Record.FirstSeen < b.Record.FirstSeen ? a : b) :
								   // else tie on the height
								   ah < bh ? a : b;
						}
						else
						{
							return a;
						}
					}
					else if (b.Height is int bh)
					{
						return b;
					}
					// Both unconfirmed, tie on firstSeen
					else
					{
						return (a.Record.FirstSeen < b.Record.FirstSeen ? a : b);
					}
				});
		}
		public static ICollection<T> TopologicalSort<T>(this ICollection<T> nodes, Func<T, IEnumerable<T>> dependsOn)
		{
			return nodes.TopologicalSort(dependsOn, k => k, k => k);
		}

		public static ICollection<T> TopologicalSort<T, TDepend>(this ICollection<T> nodes, Func<T, IEnumerable<TDepend>> dependsOn, Func<T, TDepend> getKey)
		{
			return nodes.TopologicalSort(dependsOn, getKey, o => o);
		}

		public static ICollection<TValue> TopologicalSort<T, TDepend, TValue>(this ICollection<T> nodes,
												Func<T, IEnumerable<TDepend>> dependsOn,
												Func<T, TDepend> getKey,
												Func<T, TValue> getValue,
												Func<T,T,T> solveTies = null)
		{
			if (nodes.Count == 0)
				return Array.Empty<TValue>();
			if (getKey == null)
				throw new ArgumentNullException(nameof(getKey));
			if (getValue == null)
				throw new ArgumentNullException(nameof(getValue));
			solveTies = solveTies ?? new Func<T, T, T>((aa, bb) => aa);
			List<TValue> result = new List<TValue>(nodes.Count);
			HashSet<TDepend> allKeys = new HashSet<TDepend>(nodes.Count);
			foreach (var node in nodes)
				allKeys.Add(getKey(node));
			var elems = nodes.ToDictionary(node => node,
										   node => new HashSet<TDepend>(dependsOn(node).Where(n => allKeys.Contains(n))));
			var elem = elems.FirstOrDefault(x => x.Value.Count == 0);
			if (elem.Key == null)
			{
				throw new InvalidOperationException("Impossible to topologically sort a cyclic graph");
			}
			while (elems.Count > 0)
			{
				elems.Remove(elem.Key);
				result.Add(getValue(elem.Key));
				KeyValuePair<T, HashSet<TDepend>>? nextElement = null;
				foreach (var selem in elems)
				{
					selem.Value.Remove(getKey(elem.Key));
					if (selem.Value.Count == 0)
					{
						if (nextElement is null)
						{
							nextElement = selem;
						}
						else
						{
							var preferred = solveTies(selem.Key, nextElement.Value.Key);
							nextElement = ReferenceEquals(preferred, selem.Key) ? selem : nextElement;
						}
					}
				}
				if (nextElement is KeyValuePair<T, HashSet<TDepend>> n)
					elem = n;
				else if (elems.Count != 0)
				{
					throw new InvalidOperationException("Impossible to topologically sort a cyclic graph");
				}
			}
			return result;
		}

		public static TransactionResult ToTransactionResult(bool includeTransaction, SlimChain chain, Repository.SavedTransaction[] result, Network network)
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

			var tx = new TransactionResult() { Confirmations = conf, BlockId = confBlock?.Hash, Transaction = oldest.Transaction, TransactionHash = oldest.Transaction.GetHash(), Height = confBlock?.Height, Timestamp = oldest.Timestamp };
			tx.Inputs.AddRange(tx.Transaction.Inputs.Select(i => new TransactionInput() { ScriptPubKey = i.GetSigner()?.ScriptPubKey, Index = (int)i.PrevOut.N, TransactionHash = i.PrevOut.Hash }));
			tx.Outputs.AddRange(tx.Transaction.Outputs.Select(i => new TransactionOutput() { ScriptPubKey = i.ScriptPubKey, Value = i.Value }));
			foreach(var i in tx.Inputs)
			{
				i.Address = i.ScriptPubKey?.GetDestinationAddress(network)?.ToString();
			}
			foreach (var i in tx.Outputs)
			{
				i.Address = i.ScriptPubKey.GetDestinationAddress(network)?.ToString();
			}
			if (!includeTransaction)
				tx.Transaction = null;
			return tx;
		}
	}
}
