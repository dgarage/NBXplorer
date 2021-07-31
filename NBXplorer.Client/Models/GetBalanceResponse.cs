using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class GetBalanceResponse
	{
		public IMoney Unconfirmed { get; set; }
		public IMoney Confirmed { get; set; }
		public IMoney Immature { get; set; }
		public IMoney Total { get; set; }
	}
}
