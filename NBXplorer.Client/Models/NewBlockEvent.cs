using NBitcoin;

namespace NBXplorer.Models
{
	public class NewBlockEvent : NewEventBase
	{
		public int Height
		{
			get; set;
		}

		public uint256 Hash
		{
			get; set;
		}
		public uint256 PreviousBlockHash
		{
			get; set;
		}

		public override string ToString()
		{
			return $"{CryptoCode}: New block {Hash} ({Height})";
		}
	}
}
