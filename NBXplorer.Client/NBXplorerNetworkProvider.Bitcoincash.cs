using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitBCash(ChainType chainType)
		{
			NBXplorer.Altcoins.BCash.Networks.EnsureRegistered();
			Add(new NBXplorerNetwork()
			{
				CryptoCode = "BCH",
				MinRPCVersion = 140200,
				NBitcoinNetwork = chainType == ChainType.Main ? NBXplorer.Altcoins.BCash.Networks.Mainnet:
								  chainType == ChainType.Test ? NBXplorer.Altcoins.BCash.Networks.Testnet :
								  chainType == ChainType.Regtest ? NBXplorer.Altcoins.BCash.Networks.Regtest : throw new NotSupportedException(chainType.ToString()),
				DefaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(chainType)
			});
		}

		public NBXplorerNetwork GetBCH()
		{
			return GetFromCryptoCode("BCH");
		}
	}
}
