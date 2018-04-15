using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitPolis(ChainType chainType)
		{
			NBXplorer.Altcoins.Polis.Networks.EnsureRegistered();
			Add(new NBXplorerNetwork()
			{
				CryptoCode = "POLIS",
				MinRPCVersion = 1030000,
				NBitcoinNetwork = chainType == ChainType.Main ? NBXplorer.Altcoins.Polis.Networks.Mainnet:
								  chainType == ChainType.Test ? NBXplorer.Altcoins.Polis.Networks.Testnet :
								  chainType == ChainType.Regtest ? NBXplorer.Altcoins.Polis.Networks.Regtest : throw new NotSupportedException(chainType.ToString()),
				DefaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(chainType)
			});
		}

		public NBXplorerNetwork GetPOLIS()
		{
			return GetFromCryptoCode("POLIS");
		}
	}
}
