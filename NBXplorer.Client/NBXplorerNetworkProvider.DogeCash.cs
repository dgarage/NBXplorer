using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitDogeCash(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.DogeCash.Instance, networkType)
			{
				MinRPCVersion = 5000000,
				CoinType = networkType == NetworkType.Mainnet ? new KeyPath("385'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetDOGEC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.DogeCash.Instance.CryptoCode);
		}
	}
}
