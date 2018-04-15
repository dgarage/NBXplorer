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

			//Tests of DOGE are broken because it outpoint locking seems to work differently
			//CryptoCode = "DOGE";
			//nodeDownloadData = NodeDownloadData.Dogecoin.v1_10_0;
			//Network = NBitcoin.Altcoins.Dogecoin.Regtest;
			//RPCSupportSegwit = false;
			//RPCStringAmount = false;

			//CryptoCode = "DASH";
			//nodeDownloadData = NodeDownloadData.Dash.v0_12_2;
			//Network = NBitcoin.Altcoins.Dash.Regtest;
			//RPCSupportSegwit = false;

			CryptoCode = "BTC";
			nodeDownloadData = NodeDownloadData.Bitcoin.v0_16_0;
			Network = NBitcoin.Network.RegTest;
			RPCSupportSegwit = true;
		}
	}
}
