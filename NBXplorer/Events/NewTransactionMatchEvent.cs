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
			if(strategy.Length > 45)
			{
				strategy = strategy.Substring(0, 20) + "..." + strategy.Substring(strategy.Length - 20);
			}
			return $"Money received in {strategy} ({conf})";
		}
	}
}
