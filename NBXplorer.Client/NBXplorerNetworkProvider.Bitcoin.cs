using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
    public partial class NBXplorerNetworkProvider
    {
		private void InitBitcoin(ChainType chainType)
		{
			Add(new Configuration.NBXplorerNetwork()
			{
				CryptoCode = "BTC",
				MinRPCVersion = 150000,
				NBitcoinNetwork = chainType == ChainType.Main ? Network.Main :
								  chainType == ChainType.Test ? Network.TestNet :
								  chainType == ChainType.Regtest ? Network.RegTest : throw new NotSupportedException(chainType.ToString()),
				DefaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(chainType)
			});
		}
	}
}
