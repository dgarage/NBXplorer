
using NBitcoin;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
        private void InitZclassic(NetworkType networkType)
        {
            Add(new NBXplorerNetwork(NBitcoin.Altcoins.Zclassic.Instance, networkType)
            {
                MinRPCVersion = 140200
            });
        }
        public NBXplorerNetwork GetZCL()
        {
            return GetFromCryptoCode(NBitcoin.Altcoins.Zclassic.Instance.CryptoCode);
        }
    }
}