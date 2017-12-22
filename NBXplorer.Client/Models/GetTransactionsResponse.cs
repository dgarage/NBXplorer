using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
    public class GetTransactionsResponse
    {
		public uint256 Hash
		{
			get; set;
		}

		public uint256[] TxIds
		{
			get; set;
		}
	}
}
