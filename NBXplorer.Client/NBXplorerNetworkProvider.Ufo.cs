using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitUfo(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Ufo.Instance, networkType)
			{
				MinRPCVersion = 150000,
				CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("202'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetUFO()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Ufo.Instance.CryptoCode);
		}
	}
}
