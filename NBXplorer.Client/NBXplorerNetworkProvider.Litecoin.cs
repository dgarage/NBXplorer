using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitLitecoin(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Litecoin.Instance, networkType)
			{
				MinRPCVersion = 140200
			});
		}

		public NBXplorerNetwork GetLTC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Litecoin.Instance.CryptoCode);
		}
	}
}
