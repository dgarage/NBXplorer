using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitGobyte(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.GoByte.Instance, networkType)
			{
				MinRPCVersion = 120204
			});
		}

		public NBXplorerNetwork GetGBX()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.GoByte.Instance.CryptoCode);
		}
	}
}
