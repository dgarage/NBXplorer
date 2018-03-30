using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitFeathercoin(ChainType chainType)
		{
			NBXplorer.Altcoins.Feathercoin.Networks.EnsureRegistered();
			Add(new NBXplorerNetwork()
			{
				CryptoCode = "FTC",
				MinRPCVersion = 130000,
				NBitcoinNetwork = chainType == ChainType.Main ? NBXplorer.Altcoins.Feathercoin.Networks.Mainnet:
								  chainType == ChainType.Test ? NBXplorer.Altcoins.Feathercoin.Networks.Testnet :
								  chainType == ChainType.Regtest ? NBXplorer.Altcoins.Feathercoin.Networks.Regtest : throw new NotSupportedException(chainType.ToString()),
				DefaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(chainType)
			});
		}

		public NBXplorerNetwork GetFTC()
		{
			return GetFromCryptoCode("FTC");
		}
	}
}
