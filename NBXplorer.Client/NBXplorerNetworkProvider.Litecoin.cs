using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitLitecoin(ChainType chainType)
		{
			NBitcoin.Altcoins.Litecoin.EnsureRegistered();
			Add(new NBXplorerNetwork()
			{
				CryptoCode = "LTC",
				MinRPCVersion = 140200,
				NBitcoinNetwork = chainType == ChainType.Main ? NBitcoin.Altcoins.Litecoin.Mainnet:
								  chainType == ChainType.Test ? NBitcoin.Altcoins.Litecoin.Testnet :
								  chainType == ChainType.Regtest ? NBitcoin.Altcoins.Litecoin.Regtest : throw new NotSupportedException(chainType.ToString()),
				DefaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(chainType)
			});
		}

		public NBXplorerNetwork GetLTC()
		{
			return GetFromCryptoCode("LTC");
		}
	}
}
