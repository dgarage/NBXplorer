using NBitcoin;

namespace NBXplorer
{
	public partial class NBXplorerNetworkProvider
    {
		private void InitGroestlcoin(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Groestlcoin.Instance, networkType)
			{
				MinRPCVersion = 150000,
				CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("17'") : new KeyPath("1'")
			});
		}
	}
}
