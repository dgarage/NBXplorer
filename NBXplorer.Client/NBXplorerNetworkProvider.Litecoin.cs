using NBXplorer.Altcoins.Litecoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitLitecoin(ChainType chainType)
		{
			Networks.EnsureRegistered();
			Add(new Configuration.NBXplorerNetwork()
			{
				CryptoCode = "LTC",
				MinRPCVersion = 140200,
				NBitcoinNetwork = chainType == ChainType.Main ? Networks.Mainnet:
								  chainType == ChainType.Test ? Networks.Testnet :
								  chainType == ChainType.Regtest ? Networks.Regtest : throw new NotSupportedException(chainType.ToString()),
				DefaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(chainType)
			});
		}
	}
}
