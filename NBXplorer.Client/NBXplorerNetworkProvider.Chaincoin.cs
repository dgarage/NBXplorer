using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
	public partial class NBXplorerNetworkProvider
	{
		private void InitChaincoin(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Chaincoin.Instance, networkType)
			{
				MinRPCVersion = 160400,
				CoinType = networkType == ChainName.Mainnet ? new KeyPath("711'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetCHC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Chaincoin.Instance.CryptoCode);
		}
	}
}
