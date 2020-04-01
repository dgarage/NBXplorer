using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class TransactionResult
	{
		int _Confirmations;
		public int Confirmations
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

		public int? Height
		{
			get;
			set;
		}
		public DateTimeOffset Timestamp
		{
			get;
			set;
		}
	}
}
