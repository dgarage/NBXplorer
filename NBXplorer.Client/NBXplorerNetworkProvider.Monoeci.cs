using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitMonoeci(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Monoeci.Instance, networkType)
			{
				MinRPCVersion = 120203
			});
		}

		public NBXplorerNetwork GetXMCC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Monoeci.Instance.CryptoCode);
		}
	}
}
