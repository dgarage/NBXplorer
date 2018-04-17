using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitPolis(ChainType chainType)
		{
			NBitcoin.Altcoins.Polis.EnsureRegistered();
			Add(new NBXplorerNetwork()
			{
				CryptoCode = "POLIS",
				MinRPCVersion = 1030000,
				NBitcoinNetwork = chainType == ChainType.Main ? NBitcoin.Altcoins.Polis.Mainnet :
								  chainType == ChainType.Test ? NBitcoin.Altcoins.Polis.Testnet :
								  chainType == ChainType.Regtest ? NBitcoin.Altcoins.Polis.Regtest : throw new NotSupportedException(chainType.ToString()),
				DefaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(chainType)
			});
		}

		public NBXplorerNetwork GetPOLIS()
		{
			return GetFromCryptoCode("POLIS");
		}
	}
}
