using NBitcoin;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitDash(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Dash.Instance, networkType)
			{
				MinRPCVersion = 120000,
				CoinType = networkType == ChainName.Mainnet ? new KeyPath("5'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetDASH()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Dash.Instance.CryptoCode);
		}
	}
}
