using NBitcoin;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitArgoneum(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Argoneum.Instance, networkType)
			{
				MinRPCVersion = 1040000,
				CoinType = networkType == ChainName.Mainnet ? new KeyPath("421'") : new KeyPath("1'")
			});
		}

		public NBXplorerNetwork GetAGM()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Argoneum.Instance.CryptoCode);
		}
	}
}
