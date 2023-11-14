using NBitcoin;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitLitecoin(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Litecoin.Instance, networkType)
			{
				MinRPCVersion = 140200,
				CoinType = networkType == ChainName.Mainnet ? new KeyPath("2'") : new KeyPath("1'"),
			});
		}

		public NBXplorerNetwork GetLTC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Litecoin.Instance.CryptoCode);
		}
	}
}
