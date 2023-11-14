using NBitcoin;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitMonoeci(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Monoeci.Instance, networkType)
			{
				MinRPCVersion = 120203,
				CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("1998'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetXMCC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Monoeci.Instance.CryptoCode);
		}
	}
}
