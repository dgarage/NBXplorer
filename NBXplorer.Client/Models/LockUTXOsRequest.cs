using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
    public class LockUTXOsRequest
    {
		public Money Amount
		{
			get; set;
		}
		public string Destination
		{
			get; set;
		}
		public FeeRate FeeRate
		{
			get;
			set;
		}
	}
}
