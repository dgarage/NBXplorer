using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
    public class LockUTXOsResponse
    {
		public class ChangeInfo
		{
			public KeyPath KeyPath
			{
				get; set;
			}
			public Money Value
			{
				get; set;
			}
		}

		public class SpentCoin
		{
			public KeyPath KeyPath
			{
				get; set;
			}

			public Money Value
			{
				get; set;
			}

			public OutPoint Outpoint
			{
				get; set;
			}
		}
		public SpentCoin[] SpentCoins
		{
			get; set;
		}

		public ChangeInfo ChangeInformation
		{
			get; set;
		}

		public Transaction Transaction
		{
			get; set;
		}
	}
}
