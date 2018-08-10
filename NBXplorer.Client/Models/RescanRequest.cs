using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
    public class RescanRequest
    {
		public class TransactionToRescan
		{
			public uint256 BlockId
			{
				get; set;
			}
			public uint256 TransactionId
			{
				get; set;
			}
			public Transaction Transaction
			{
				get; set;
			}
		}
		public List<TransactionToRescan> Transactions
		{
			get; set;
		} = new List<TransactionToRescan>();
	}
}
