using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class RawBlockEvent
	{
		public RawBlockEvent(Block block, NBXplorerNetwork network)
		{
			Block = block;
			Network = network;
		}
		public Block Block { get; set; }
		public NBXplorerNetwork Network { get; set; }
	}
}
