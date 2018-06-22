using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
    public class GetBalanceResponse
    {
		public NBitcoin.Money Spendable
		{
			get; set;
		}

		public NBitcoin.Money Total
		{
			get; set;
		}
	}
}
