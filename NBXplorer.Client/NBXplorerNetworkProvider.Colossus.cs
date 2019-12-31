using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitColossus(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Colossus.Instance, networkType)
			{
				MinRPCVersion = 1010000
			});
		}

		public NBXplorerNetwork GetCOLX()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Colossus.Instance.CryptoCode);
		}
	}
}
