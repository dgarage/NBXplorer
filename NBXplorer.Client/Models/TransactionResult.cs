using NBitcoin;
using System;

namespace NBXplorer.Models
{
	public class TransactionResult
	{
		long _Confirmations;
		public long Confirmations
		{
			get
			{
				return _Confirmations;
			}
			set
			{
				_Confirmations = value;
			}
		}

		uint256 _BlockId;
		public uint256 BlockId
		{
			get
			{
				return _BlockId;
			}
			set
			{
				_BlockId = value;
			}
		}
		public uint256 TransactionHash { get; set; }
		Transaction _Transaction;
		public Transaction Transaction
		{
			get
			{
				return _Transaction;
			}
			set
			{
				_Transaction = value;
			}
		}

		public long? Height
		{
			get;
			set;
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
