using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class TransactionInput
	{
		public int Index { get; set; }
		public uint256 TransactionHash { get; set; }
		public Script ScriptPubKey { get; set; }
		public string Address { get; set; }
	}
	public class TransactionOutput
	{
		public Script ScriptPubKey { get; set; }
		public string Address { get; set; }
		public Money Value { get; set; }
	}
	public class TransactionResult
	{
		uint _Confirmations;
		public int Confirmations
		{
			get
			{
				return checked((int)_Confirmations);
			}
			set
			{
				_Confirmations = checked((uint)value);
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

		public List<TransactionInput> Inputs { get; set; } = new List<TransactionInput>();
		public List<TransactionOutput> Outputs { get; set; } = new List<TransactionOutput>();

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
