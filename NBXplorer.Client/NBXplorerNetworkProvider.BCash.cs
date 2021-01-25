using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
	public partial class NBXplorerNetworkProvider
	{
		private void InitBCash(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.BCash.Instance, networkType)
			{
				MinRPCVersion = 140200,
				CoinType = networkType == ChainName.Mainnet ? new KeyPath("145'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetBCH()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.BCash.Instance.CryptoCode);
		}
	}
}
