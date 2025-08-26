#nullable enable
using NBitcoin;
using NBitcoin.Altcoins.Elements;
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
		public MatchQuery(IEnumerable<ICoin> coins)
		{
			Outs = coins.Select(c => DbConnectionHelper.NewOut.FromCoin(c)).ToList();
			Ins = new List<DbConnectionHelper.NewIn>();
		}
		public List<DbConnectionHelper.NewIn> Ins { get; }
		public List<DbConnectionHelper.NewOut> Outs { get; }

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
					IMoney val = output switch
					{
						ElementsTxOut { Asset: { AssetId: { } assetId } } el => new AssetMoney(assetId, el.Value),
						_ => output.Value
					};
					outs.Add(new DbConnectionHelper.NewOut(hash, io, output.ScriptPubKey, val));
				}
			}
			return new MatchQuery(ins, outs);
		}
	}
}
