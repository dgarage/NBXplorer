using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer.Events
{
    public class NewBlockEvent
    {
		public NewBlockEvent()
		{

		}
		public NewBlockEvent(string cryptoCode, uint256 block)
		{
			BlockId = block;
			CryptoCode = cryptoCode;
		}

		public string CryptoCode
		{
			get; set;
		}

		public uint256 BlockId
		{
			get; set;
		}

		public override string ToString()
		{
			return $"{CryptoCode}: New block " + BlockId;
		}
	}
}
