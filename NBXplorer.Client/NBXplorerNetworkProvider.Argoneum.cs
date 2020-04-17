using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitArgoneum(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Argoneum.Instance, networkType)
			{
				MinRPCVersion = 1040000,
				CoinType = networkType == NetworkType.Mainnet ? new KeyPath("421'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetAGM()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Argoneum.Instance.CryptoCode);
		}
	}
}
