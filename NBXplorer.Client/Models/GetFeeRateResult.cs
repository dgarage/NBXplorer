using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public class GetFeeRateResult
    {
		public FeeRate FeeRate
		{
			get; set;
		}

		public int BlockCount
		{
			get; set;
		}
	}
}
