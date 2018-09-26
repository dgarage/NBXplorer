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
			//Network = NBitcoin.Altcoins.Litecoin.Instance.Regtest;
			//RPCSupportSegwit = false;

			//CryptoCode = "BCH";
			//nodeDownloadData = NodeDownloadData.BCash.v0_16_2;
			//Network = NBitcoin.Altcoins.BCash.Instance.Regtest;
			//RPCSupportSegwit = false;

			//Tests of DOGE are broken because it outpoint locking seems to work differently
			//CryptoCode = "DOGE";
			//nodeDownloadData = NodeDownloadData.Dogecoin.v1_10_0;
			//Network = NBitcoin.Altcoins.Dogecoin.Instance.Regtest;
			//RPCSupportSegwit = false;
			//RPCStringAmount = false;

			//CryptoCode = "DASH";
			//nodeDownloadData = NodeDownloadData.Dash.v0_12_2;
			//Network = NBitcoin.Altcoins.Dash.Instance.Regtest;
			//RPCSupportSegwit = false;

			//CryptoCode = "Polis";
			//nodeDownloadData = NodeDownloadData.Polis.v1_3_0;
			//Network = NBitcoin.Altcoins.Polis.Instance.Regtest;
			//RPCSupportSegwit = false;

			//CryptoCode = "BTG";
			//nodeDownloadData = NodeDownloadData.BGold.v0_15_0;
			//Network = NBitcoin.Altcoins.BGold.Instance.Regtest;
			//RPCSupportSegwit = false;

			//CryptoCode = "MONA";
			//nodeDownloadData = NodeDownloadData.Monacoin.v0_15_1;
			//Network = NBitcoin.Altcoins.Monacoin.Instance.Regtest;
			//RPCSupportSegwit = false;

			//CryptoCode = "FTC";
			//nodeDownloadData = NodeDownloadData.Feathercoin.v0_16_0;
			//Network = NBitcoin.Altcoins.Feathercoin.Instance.Regtest;
			//RPCSupportSegwit = false;

			//CryptoCode = "UFO";
			//nodeDownloadData = NodeDownloadData.Ufo.v0_16_0;
			//Network = NBitcoin.Altcoins.Ufo.Instance.Regtest;
			//RPCSupportSegwit = false;

			//CryptoCode = "VIA";
			//nodeDownloadData = NodeDownloadData.Viacoin.v0_15_1;
			//Network = NBitcoin.Altcoins.Viacoin.Instance.Regtest;
			//RPCSupportSegwit = false;

			//CryptoCode = "GRS";
			//nodeDownloadData = NodeDownloadData.Groestlcoin.v2_16_0;
			//Network = NBitcoin.Altcoins.Groestlcoin.Instance.Regtest;

			//CryptoCode = "NAV";
			//nodeDownloadData = NodeDownloadData.Viacoin.v4_3_0;
			//Network = NBitcoin.Altcoins.Navcoin.Instance.Regtest;

			CryptoCode = "BTC";
			nodeDownloadData = NodeDownloadData.Bitcoin.v0_16_0;
			Network = NBitcoin.Network.RegTest;
			RPCSupportSegwit = true;
		}
	}
}
