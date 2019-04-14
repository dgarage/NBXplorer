using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitStratis(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Stratis.Instance, networkType)
			{
				MinRPCVersion = 3000004,
				SupportCookieAuthentication = false
			});
		}

		public NBXplorerNetwork GetSTRAT()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Stratis.Instance.CryptoCode);
		}
	}
}
