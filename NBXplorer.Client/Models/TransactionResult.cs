using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
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
