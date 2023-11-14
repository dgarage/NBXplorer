using NBitcoin;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitPolis(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Polis.Instance, networkType)
			{
				MinRPCVersion = 1030000,
				CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("1997'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetPOLIS()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Polis.Instance.CryptoCode);
		}
	}
}
