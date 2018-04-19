using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitDogecoin(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Dogecoin.Instance, networkType)
			{
				MinRPCVersion = 140200,
				ChainLoadingTimeout = TimeSpan.FromHours(1),
				SupportCookieAuthentication = false
			});
		}

		public NBXplorerNetwork GetDOGE()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Dogecoin.Instance.CryptoCode);
		}
	}
}
