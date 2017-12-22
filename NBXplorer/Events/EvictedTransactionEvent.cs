using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer.Events
{
    public class EvictedTransactionEvent
    {
		public EvictedTransactionEvent(uint256 tx)
		{
			TransactionId = tx;
		}

		public uint256 TransactionId
		{
			get; set;
		}

		public override string ToString()
		{
			var txId = TransactionId.ToString();
			txId = txId.Substring(0, 6) + "..." + txId.Substring(txId.Length - 6);
			return $"Transaction {txId} is evicted from local mempool";
		}
	}
}
