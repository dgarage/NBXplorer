using NBitcoin;
using System;

namespace NBXplorer.Backends
{
	public class SavedTransaction
	{
		public NBitcoin.Transaction Transaction
		{
			get; set;
		}
		public uint256 BlockHash
		{
			get; set;
		}
		public long? BlockHeight
		{
			get; set;
		}
		public DateTimeOffset Timestamp
		{
			get;
			set;
		}
	}
}
