using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitAlthash(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Althash.Instance, networkType)
			{
				MinRPCVersion = 169900,
				CoinType = networkType == ChainName.Mainnet ? new KeyPath("172'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetALTHASH()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Althash.Instance.CryptoCode);
		}
	}
}
