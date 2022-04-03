using NBitcoin.RPC;

namespace NBXplorer
{
	public interface IRPCClients
	{
		RPCClient Get(NBXplorerNetwork network);
	}
}
