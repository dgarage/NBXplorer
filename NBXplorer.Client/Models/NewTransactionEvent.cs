using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using NBXplorer.DerivationStrategy;

namespace NBXplorer.Models
{
	public class NewTransactionEvent
	{
		public uint256 BlockId
		{
			get; set;
		}

		public TransactionMatch Match
		{
			get; set;
		}
	}
}
