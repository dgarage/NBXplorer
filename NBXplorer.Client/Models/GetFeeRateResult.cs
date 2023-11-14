using NBitcoin;

namespace NBXplorer.Models
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
