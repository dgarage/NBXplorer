using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitQtum(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Qtum.Instance, networkType)
			{
				MinRPCVersion = 140200,
				CoinType = networkType == NetworkType.Mainnet ? new KeyPath("2301'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetQTUM()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Qtum.Instance.CryptoCode);
		}
	}
}
