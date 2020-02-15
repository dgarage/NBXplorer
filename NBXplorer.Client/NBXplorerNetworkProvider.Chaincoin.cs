using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
	public partial class NBXplorerNetworkProvider
	{
		private void InitChaincoin(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Chaincoin.Instance, networkType)
			{
				MinRPCVersion = 160400,
				CoinType = networkType == NetworkType.Mainnet ? new KeyPath("711'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetCHC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Chaincoin.Instance.CryptoCode);
		}
	}
}
