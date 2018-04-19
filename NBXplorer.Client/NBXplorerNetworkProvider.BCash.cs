using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitBCash(ChainType chainType)
		{
			NBitcoin.Altcoins.BCash.EnsureRegistered();
			Add(new NBXplorerNetwork()
			{
				MinRPCVersion = 140200,
				NBitcoinNetwork = chainType == ChainType.Main ? NBitcoin.Altcoins.BCash.Mainnet:
								  chainType == ChainType.Test ? NBitcoin.Altcoins.BCash.Testnet :
								  chainType == ChainType.Regtest ? NBitcoin.Altcoins.BCash.Regtest : throw new NotSupportedException(chainType.ToString()),
				DefaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(chainType)
			});
		}

		public NBXplorerNetwork GetBCH()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.BCash.Instance.CryptoCode);
		}
	}
}
