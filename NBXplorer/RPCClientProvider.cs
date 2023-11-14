using NBitcoin.RPC;
using NBXplorer.Configuration;
using System.Collections.Generic;
using System.Net.Http;

namespace NBXplorer
{
	public class RPCClientProvider : IRPCClients
	{
		Dictionary<string, RPCClient> _ChainConfigurations = new Dictionary<string, RPCClient>();
		public RPCClientProvider(ExplorerConfiguration configuration, IHttpClientFactory httpClientFactory)
		{
			foreach(var config in configuration.ChainConfigurations)
			{
				var rpc = config?.RPC;
				if (rpc != null)
				{
					rpc.HttpClient = httpClientFactory.CreateClient(nameof(IRPCClients));
					_ChainConfigurations.Add(config.CryptoCode, rpc);
				}
			}
		}

		public RPCClient Get(NBXplorerNetwork network)
		{
			_ChainConfigurations.TryGetValue(network.CryptoCode, out RPCClient rpc);
			return rpc;
		}
	}
}
