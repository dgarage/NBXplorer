using NBitcoin;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitTerracoin(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Terracoin.Instance, networkType)
			{
				MinRPCVersion = 120204,
				CoinType = networkType == ChainName.Mainnet ? new KeyPath("83'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetTRC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Terracoin.Instance.CryptoCode);
		}
	}
}
