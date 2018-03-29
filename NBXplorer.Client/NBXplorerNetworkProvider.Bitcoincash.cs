using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitBitcoincash(ChainType chainType)
		{
			NBXplorer.Altcoins.Bitcoincash.Networks.EnsureRegistered();
			Add(new NBXplorerNetwork()
			{
				CryptoCode = "BCH",
				MinRPCVersion = 140200,
				NBitcoinNetwork = chainType == ChainType.Main ? NBXplorer.Altcoins.Bitcoincash.Networks.Mainnet:
								  chainType == ChainType.Test ? NBXplorer.Altcoins.Bitcoincash.Networks.Testnet :
								  chainType == ChainType.Regtest ? NBXplorer.Altcoins.Bitcoincash.Networks.Regtest : throw new NotSupportedException(chainType.ToString()),
				DefaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(chainType)
			});
		}

		public NBXplorerNetwork GetBCH()
		{
			return GetFromCryptoCode("BCH");
		}
	}
}
