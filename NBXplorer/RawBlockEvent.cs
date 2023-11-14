using NBitcoin;

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
