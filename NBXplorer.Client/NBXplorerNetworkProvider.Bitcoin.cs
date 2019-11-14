using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitBitcoin(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Bitcoin.Instance, networkType)
			{
				MinRPCVersion = 150000,
				CoinType = networkType == NetworkType.Mainnet ? new KeyPath("0'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetBTC()
		{
			return GetFromCryptoCode(NBitcoin.Bitcoin.Instance.CryptoCode);
		}
	}
}
