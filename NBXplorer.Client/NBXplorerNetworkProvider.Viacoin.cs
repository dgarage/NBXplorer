using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitViacoin(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Viacoin.Instance, networkType)
			{
				MinRPCVersion = 140200,
				CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("14'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetVIA()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Viacoin.Instance.CryptoCode);
		}
	}
}