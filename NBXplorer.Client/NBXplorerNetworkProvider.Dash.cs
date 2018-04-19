using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitDash(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Dash.Instance, networkType)
			{
				MinRPCVersion = 120000
			});
		}

		public NBXplorerNetwork GetDASH()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Dash.Instance.CryptoCode);
		}
	}
}
