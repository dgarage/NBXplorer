using NBitcoin;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitMonacoin(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Monacoin.Instance, networkType)
			{
				MinRPCVersion = 140200,
				CoinType = networkType == ChainName.Mainnet ? new KeyPath("22'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetMONA()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Monacoin.Instance.CryptoCode);
		}
	}
}
