using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitPolis(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Polis.Instance, networkType)
			{
				MinRPCVersion = 1030000
			});
		}

		public NBXplorerNetwork GetPOLIS()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Polis.Instance.CryptoCode);
		}
	}
}
