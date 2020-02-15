using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitTerracoin(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Terracoin.Instance, networkType)
			{
				MinRPCVersion = 120204
			});
		}

		public NBXplorerNetwork GetTRC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Terracoin.Instance.CryptoCode);
		}
	}
}
