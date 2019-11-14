using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitGobyte(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.GoByte.Instance, networkType)
			{
				MinRPCVersion = 120204,
				CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("176'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetGBX()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.GoByte.Instance.CryptoCode);
		}
	}
}
