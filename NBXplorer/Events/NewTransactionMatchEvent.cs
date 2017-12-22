using NBitcoin;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer.Events
{
    public class NewTransactionMatchEvent
    {
		public NewTransactionMatchEvent(uint256 blockId, TransactionMatch match)
		{
			Match = match;
			BlockId = blockId;
		}

		public uint256 BlockId
		{
			get; set;
		}

		public TransactionMatch Match
		{
			get; set;
		}

		public override string ToString()
		{
			var conf = (BlockId == null ? "Unconfirmed" : "Confirmed");

			var strategy = Match.DerivationStrategy.ToString();
			if(strategy.Length > 35)
			{
				strategy = strategy.Substring(0, 10) + "..." + strategy.Substring(strategy.Length - 20);
			}
			var txId = Match.Transaction.GetHash().ToString();
			txId = txId.Substring(0, 6) + "..." + txId.Substring(txId.Length - 6);
			return $"Money received in {strategy} in transaction {txId} ({conf})";
		}
	}
}
