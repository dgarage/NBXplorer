using NBitcoin;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NBXplorer
{
	public partial class NBXplorerNetworkProvider
	{
		private void InitBitcore(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Bitcore.Instance, networkType)
			{
				MinRPCVersion = 80007,
				CoinType = networkType == ChainName.Mainnet ? new KeyPath("160'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetBTX()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Bitcore.Instance.CryptoCode);
		}
	}
}
