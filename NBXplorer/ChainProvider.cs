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
		Dictionary<string, SlimChain> _Chains = new Dictionary<string, SlimChain>();
		public ChainProvider(ExplorerConfiguration configuration)
		{
			foreach(var net in configuration.NetworkProvider.GetAll().Where(n => configuration.Supports(n)))
			{
				_Chains.Add(net.CryptoCode, new SlimChain(net.NBitcoinNetwork.GenesisHash));
			}
		}

		public SlimChain GetChain(NBXplorerNetwork network)
		{
			return GetChain(network.CryptoCode);
		}
		public SlimChain GetChain(string network)
		{
			_Chains.TryGetValue(network, out SlimChain concurrent);
			return concurrent;
		}
	}
}
