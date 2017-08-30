using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class TransactionResult : IBitcoinSerializable
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

		public void ReadWrite(BitcoinStream stream)
		{
			stream.ReadWriteAsVarInt(ref _Confirmations);
			stream.ReadWrite(ref _Transaction);
		}
	}
}
