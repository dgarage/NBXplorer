using NBitcoin.RPC;
using NBXplorer.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class RPCClientProvider
	{
		Dictionary<string, RPCClient> _ChainConfigurations = new Dictionary<string, RPCClient>();
		public RPCClientProvider(ExplorerConfiguration configuration)
		{
			foreach(var config in configuration.ChainConfigurations)
			{
				var rpc = config?.RPC;
				if(rpc != null)
					_ChainConfigurations.Add(config.CryptoCode, rpc);
			}
		}

		public IEnumerable<RPCClient> GetAll()
		{
			return _ChainConfigurations.Values;
		}

		public RPCClient GetRPCClient(string cryptoCode)
		{
			_ChainConfigurations.TryGetValue(cryptoCode, out RPCClient rpc);
			return rpc;
		}
		public RPCClient GetRPCClient(NBXplorerNetwork network)
		{
			return GetRPCClient(network.CryptoCode);
		}
	}
}
