using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitLitecoin(ChainType chainType)
		{
			NBXplorer.Altcoins.Litecoin.Networks.EnsureRegistered();
			Add(new NBXplorerNetwork()
			{
				CryptoCode = "LTC",
				MinRPCVersion = 140200,
				NBitcoinNetwork = chainType == ChainType.Main ? NBXplorer.Altcoins.Litecoin.Networks.Mainnet:
								  chainType == ChainType.Test ? NBXplorer.Altcoins.Litecoin.Networks.Testnet :
								  chainType == ChainType.Regtest ? NBXplorer.Altcoins.Litecoin.Networks.Regtest : throw new NotSupportedException(chainType.ToString()),
				DefaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(chainType)
			});
		}

		public NBXplorerNetwork GetLTC()
		{
			return GetFromCryptoCode("LTC");
		}
	}
}
