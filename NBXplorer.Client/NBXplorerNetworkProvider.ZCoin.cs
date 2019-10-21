using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitZCoin(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.ZCoin.Instance, networkType)
			{
				MinRPCVersion = 150000
			});
		}

		public NBXplorerNetwork GetXZC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.ZCoin.Instance.CryptoCode);
		}
	}
}
