using NBitcoin;
using NBXplorer.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class ChainProvider
	{
		Dictionary<string, ConcurrentChain> _Chains = new Dictionary<string, ConcurrentChain>();
		public ChainProvider(ExplorerConfiguration configuration)
		{
			foreach(var net in configuration.NetworkProvider.GetAll().Where(n => configuration.Supports(n)))
			{
				_Chains.Add(net.CryptoCode, new ConcurrentChain(net.NBitcoinNetwork));
			}
		}

		public ConcurrentChain GetChain(NBXplorerNetwork network)
		{
			return GetChain(network.CryptoCode);
		}
		public ConcurrentChain GetChain(string network)
		{
			_Chains.TryGetValue(network, out ConcurrentChain concurrent);
			return concurrent;
		}
	}
}
