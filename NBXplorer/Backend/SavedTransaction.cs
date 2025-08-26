using NBitcoin;
using NBXplorer.Models;
using System;

namespace NBXplorer.Backend
{
	public class SavedTransaction
	{
		public uint256 TxId { get; set; }
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
		public uint256 ReplacedBy { get; set; }

		public TransactionMetadata Metadata { get; set; }
	}
}
