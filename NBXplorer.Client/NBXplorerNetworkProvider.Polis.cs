using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitPolis(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Polis.Instance, networkType)
			{
				MinRPCVersion = 1030000,
				CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("1997'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetPOLIS()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Polis.Instance.CryptoCode);
		}
	}
}
