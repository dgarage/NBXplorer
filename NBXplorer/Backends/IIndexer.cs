using NBitcoin;
using NBitcoin.RPC;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NBXplorer.Backends
{
	public interface IIndexers
	{
		IIndexer GetIndexer(NBXplorerNetwork network);
		IEnumerable<IIndexer> All();
	}
	public enum BitcoinDWaiterState
	{
		NotStarted,
		CoreSynching,
		NBXplorerSynching,
		Ready
	}
	public interface IIndexer
	{
		RPCClient GetConnectedClient();
		NBXplorerNetwork Network { get; }
		BitcoinDWaiterState State { get; }
		long? SyncHeight { get; }
		GetNetworkInfoResponse NetworkInfo { get; }
		Task SaveMatches(Transaction transaction);
	}
}
