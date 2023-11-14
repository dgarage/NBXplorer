using NBitcoin;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitColossus(ChainName networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.Colossus.Instance, networkType)
			{
				MinRPCVersion = 1010000
			});
		}

		public NBXplorerNetwork GetCOLX()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Colossus.Instance.CryptoCode);
		}
	}
}
