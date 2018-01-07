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
		public NewBlockEvent(uint256 block)
		{
			BlockId = block;
		}

		public uint256 BlockId
		{
			get; set;
		}

		public override string ToString()
		{
			return "New block " + BlockId;
		}
	}
}
