using NBitcoin;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitBGold(NetworkType networkType)
		{
			Add(new NBXplorerNetwork(NBitcoin.Altcoins.BGold.Instance, networkType)
			{
				MinRPCVersion = 140200
			});
		}

		public NBXplorerNetwork GetBTG()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.BGold.Instance.CryptoCode);
		}
	}
}
