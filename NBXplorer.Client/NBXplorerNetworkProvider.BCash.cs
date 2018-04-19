using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitBCash(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.BCash.Instance, networkType)
			{
				MinRPCVersion = 140200
			});
		}

		public NBXplorerNetwork GetBCH()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.BCash.Instance.CryptoCode);
		}
	}
}
