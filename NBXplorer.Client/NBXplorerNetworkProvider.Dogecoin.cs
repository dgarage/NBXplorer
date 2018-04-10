using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitDogecoin(ChainType chainType)
		{
			NBXplorer.Altcoins.Dogecoin.Networks.EnsureRegistered();
			Add(new NBXplorerNetwork()
			{
				CryptoCode = "DOGE",
				MinRPCVersion = 140200,
				NBitcoinNetwork = chainType == ChainType.Main ? NBXplorer.Altcoins.Dogecoin.Networks.Mainnet:
								  chainType == ChainType.Test ? NBXplorer.Altcoins.Dogecoin.Networks.Testnet :
								  chainType == ChainType.Regtest ? NBXplorer.Altcoins.Dogecoin.Networks.Regtest : throw new NotSupportedException(chainType.ToString()),
				DefaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(chainType)
			});
		}

		public NBXplorerNetwork GetDOGE()
		{
			return GetFromCryptoCode("DOGE");
		}
	}
}
