#nullable enable
using NBitcoin;
using System.Collections.Generic;
using System.Linq;

namespace NBXplorer.Backend
{
	public class MatchQuery
	{
		public MatchQuery(List<DbConnectionHelper.NewIn> ins, List<DbConnectionHelper.NewOut> outs)
		{
			Ins = ins;
			Outs = outs;
		}
		public List<DbConnectionHelper.NewIn> Ins { get; }
		public List<DbConnectionHelper.NewOut> Outs { get; }

		public static MatchQuery FromTransactions(IEnumerable<TrackedTransaction> txs, Money? minUtxoValue)
		{
			var outCount = txs.Select(t => t.ReceivedCoins.Count).Sum();
			var inCount = txs.Select(t => t.SpentOutpoints.Count).Sum();
			List<DbConnectionHelper.NewOut> outs = new List<DbConnectionHelper.NewOut>(outCount);
			List<DbConnectionHelper.NewIn> ins = new List<DbConnectionHelper.NewIn>(inCount);
			foreach (var tx in txs)
			{
				if (!tx.IsCoinBase)
				{
					foreach (var input in tx.SpentOutpoints)
					{
						ins.Add(new DbConnectionHelper.NewIn(
							tx.TransactionHash,
							input.InputIndex,
							input.Outpoint.Hash,
							(int)input.Outpoint.N
							));
					}
				}

				foreach (var output in tx.ReceivedCoins)
				{
					if (minUtxoValue != null && (Money)output.Amount < minUtxoValue)
						continue;
					outs.Add(new DbConnectionHelper.NewOut(
						tx.TransactionHash,
						(int)output.Outpoint.N,
						output.TxOut.ScriptPubKey,
						(Money)output.Amount
						));
				}
			}
			return new MatchQuery(ins, outs);
		}

		public static MatchQuery FromTransactions(IEnumerable<Transaction> txs, Money? minUtxoValue)
		{
			var outCount = txs.Select(t => t.Outputs.Count).Sum();
			List<DbConnectionHelper.NewOut> outs = new List<DbConnectionHelper.NewOut>(outCount);
			var inCount = txs.Select(t => t.Inputs.Count).Sum();
			List<DbConnectionHelper.NewIn> ins = new List<DbConnectionHelper.NewIn>(inCount);
			foreach (var tx in txs)
			{
				var hash = tx.GetHash();
				if (!tx.IsCoinBase)
				{
					int i = 0;
					foreach (var input in tx.Inputs)
					{
						ins.Add(new DbConnectionHelper.NewIn(hash, i, input.PrevOut.Hash, (int)input.PrevOut.N));
						i++;
					}
				}
				int io = -1;
				foreach (var output in tx.Outputs)
				{
					io++;
					if (minUtxoValue != null && output.Value < minUtxoValue)
						continue;
					outs.Add(new DbConnectionHelper.NewOut(hash, io, output.ScriptPubKey, output.Value));
				}
			}
			return new MatchQuery(ins, outs);
		}
	}
}
