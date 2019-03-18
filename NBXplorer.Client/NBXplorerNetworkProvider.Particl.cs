using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitParticl(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Particl.Instance, networkType)
			{
				MinRPCVersion = 170104
			});
		}

		public NBXplorerNetwork GetPART()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Particl.Instance.CryptoCode);
		}
	}
}
