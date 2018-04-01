using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		public NBXplorerNetworkProvider(ChainType chainType)
		{
			InitBitcoin(chainType);
			InitLitecoin(chainType);
			InitDogecoin(chainType);
			InitBCash(chainType);
			ChainType = chainType;
			foreach(var chain in _Networks.Values)
			{
				chain.DerivationStrategyFactory = new DerivationStrategy.DerivationStrategyFactory(chain.NBitcoinNetwork);
			}
		}

		public ChainType ChainType
		{
			get; set;
		}

		public NBXplorerNetwork GetFromCryptoCode(string cryptoCode)
		{
			_Networks.TryGetValue(cryptoCode, out NBXplorerNetwork network);
			return network;
		}

		public IEnumerable<NBXplorerNetwork> GetAll()
		{
			return _Networks.Values;
		}

		Dictionary<string, NBXplorerNetwork> _Networks = new Dictionary<string, NBXplorerNetwork>();
		private void Add(NBXplorerNetwork network)
		{
			_Networks.Add(network.CryptoCode, network);
		}
	}
}
