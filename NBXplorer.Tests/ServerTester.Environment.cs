using NBitcoin.Tests;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Tests
{
    public partial class ServerTester
    {
		private void SetEnvironment()
		{
			//CryptoCode = "LTC";
			//nodeDownloadData = NodeDownloadData.Litecoin.v0_15_1;
			//Network = NBitcoin.Altcoins.Litecoin.Regtest;
			//RPCSupportSegwit = false;

			//CryptoCode = "BCH";
			//nodeDownloadData = NodeDownloadData.BCash.v0_16_2;
			//Network = NBitcoin.Altcoins.BCash.Regtest;
			//RPCSupportSegwit = false;

			CryptoCode = "BTC";
			nodeDownloadData = NodeDownloadData.Bitcoin.v0_16_0;
			Network = NBitcoin.Network.RegTest;
			RPCSupportSegwit = true;
		}
	}
}
