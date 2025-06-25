using NBitcoin;

namespace NBXplorer
{
	public partial class NBXplorerNetworkProvider
	{
		private void InitDecred(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Decred.Instance, networkType)
			{
				MinRPCVersion = 2000600, // v2.0.6 release
				CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("42'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetDCR()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Decred.Instance.CryptoCode);
		}
	}
}
