using NBitcoin;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitFeathercoin(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Feathercoin.Instance, networkType)
			{
				MinRPCVersion = 160000,
				CoinType = networkType == ChainName.Mainnet ? new KeyPath("8'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetFTC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Feathercoin.Instance.CryptoCode);
		}
	}
}
