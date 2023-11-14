using NBitcoin;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitBitcoin(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Bitcoin.Instance, networkType)
			{
				MinRPCVersion = 150000,
				CoinType = networkType == ChainName.Mainnet ? new KeyPath("0'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetBTC()
		{
			return GetFromCryptoCode(NBitcoin.Bitcoin.Instance.CryptoCode);
		}
	}
}
