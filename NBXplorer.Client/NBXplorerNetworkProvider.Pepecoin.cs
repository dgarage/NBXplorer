using NBitcoin;
using System;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitPepecoin(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Pepecoin.Instance, networkType)
			{
				MinRPCVersion = 10000,
				ChainLoadingTimeout = TimeSpan.FromHours(1),
				ChainCacheLoadingTimeout = TimeSpan.FromMinutes(2),
				CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("3434'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetPEPE()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Pepecoin.Instance.CryptoCode);
		}
	}
}
