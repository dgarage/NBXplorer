using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitDash(ChainType chainType)
		{
			NBitcoin.Altcoins.Dash.EnsureRegistered();
			Add(new NBXplorerNetwork()
			{
				CryptoCode = "DASH",
				MinRPCVersion = 120000,
				NBitcoinNetwork = chainType == ChainType.Main ? NBitcoin.Altcoins.Dash.Mainnet :
								  chainType == ChainType.Test ? NBitcoin.Altcoins.Dash.Testnet :
								  chainType == ChainType.Regtest ? NBitcoin.Altcoins.Dash.Regtest : throw new NotSupportedException(chainType.ToString()),
				DefaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(chainType)
			});
		}

		public NBXplorerNetwork GetDASH()
		{
			return GetFromCryptoCode("DASH");
		}
	}
}
