using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitVerge(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Verge.Instance, networkType)
			{
				MinRPCVersion = 6000200,
				ChainLoadingTimeout = TimeSpan.FromHours(1),
				ChainCacheLoadingTimeout = TimeSpan.FromMinutes(3),
				CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("77'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetXVG()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Verge.Instance.CryptoCode);
		}
	}
}