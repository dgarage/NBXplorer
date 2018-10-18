using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitBitcoin(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Bitcoin.Instance, networkType)
			{
				MinRPCVersion = 150000
			});
		}

		public NBXplorerNetwork GetBTC()
		{
			return GetFromCryptoCode(NBitcoin.Bitcoin.Instance.CryptoCode);
		}
	}
}
