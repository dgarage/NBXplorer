using NBitcoin;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitBPrivate(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.BPrivate.Instance, networkType)
			{
				MinRPCVersion = 140200
			});
		}

		public NBXplorerNetwork GetBTCP()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.BPrivate.Instance.CryptoCode);
		}
	}
}
