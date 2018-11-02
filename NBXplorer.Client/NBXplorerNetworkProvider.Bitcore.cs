using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
	public partial class NBXplorerNetworkProvider
	{
		private void InitBitcore(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Bitcore.Instance, networkType)
			{
				MinRPCVersion = 150100
			});
		}

		public NBXplorerNetwork GetBTX()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Bitcore.Instance.CryptoCode);
		}
	}
}
