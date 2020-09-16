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
				MinRPCVersion = 5020000,
				CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("385'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetDOGEC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.DogeCash.Instance.CryptoCode);
		}
	}
}