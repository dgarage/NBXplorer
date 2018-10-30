using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitNavcoin(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Navcoin.Instance, networkType)
			{
				MinRPCVersion = 4030000
			});
		}

		public NBXplorerNetwork GetNAV()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Navcoin.Instance.CryptoCode);
		}
	}
}