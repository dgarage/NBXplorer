using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitFeathercoin(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Feathercoin.Instance, networkType)
			{
				MinRPCVersion = 160000,
				CoinType = networkType == NetworkType.Mainnet ? new KeyPath("8'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetFTC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Feathercoin.Instance.CryptoCode);
		}
	}
}
