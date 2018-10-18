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
			//nodeDownloadData = NodeDownloadData.Litecoin.v0_16_3;
			//Network = NBitcoin.Altcoins.Litecoin.Instance.Regtest;

			//CryptoCode = "BCH";
			//nodeDownloadData = NodeDownloadData.BCash.v0_16_2;
			//Network = NBitcoin.Altcoins.BCash.Instance.Regtest;

			//Tests of DOGE are broken because it outpoint locking seems to work differently
			//CryptoCode = "DOGE";
			//nodeDownloadData = NodeDownloadData.Dogecoin.v1_10_0;
			//Network = NBitcoin.Altcoins.Dogecoin.Instance.Regtest;
			//RPCStringAmount = false;

			//CryptoCode = "DASH";
			//nodeDownloadData = NodeDownloadData.Dash.v0_12_2;
			//Network = NBitcoin.Altcoins.Dash.Instance.Regtest;

			//CryptoCode = "Polis";
			//nodeDownloadData = NodeDownloadData.Polis.v1_3_0;
			//Network = NBitcoin.Altcoins.Polis.Instance.Regtest;

			//CryptoCode = "BTG";
			//nodeDownloadData = NodeDownloadData.BGold.v0_15_0;
			//Network = NBitcoin.Altcoins.BGold.Instance.Regtest;

			//CryptoCode = "MONA";
			//nodeDownloadData = NodeDownloadData.Monacoin.v0_15_1;
			//Network = NBitcoin.Altcoins.Monacoin.Instance.Regtest;

			//CryptoCode = "FTC";
			//nodeDownloadData = NodeDownloadData.Feathercoin.v0_16_0;
			//Network = NBitcoin.Altcoins.Feathercoin.Instance.Regtest;

			//CryptoCode = "UFO";
			//nodeDownloadData = NodeDownloadData.Ufo.v0_16_0;
			//Network = NBitcoin.Altcoins.Ufo.Instance.Regtest;

			//CryptoCode = "VIA";
			//nodeDownloadData = NodeDownloadData.Viacoin.v0_15_1;
			//Network = NBitcoin.Altcoins.Viacoin.Instance.Regtest;

			//CryptoCode = "GRS";
			//nodeDownloadData = NodeDownloadData.Groestlcoin.v2_16_0;
			//Network = NBitcoin.Altcoins.Groestlcoin.Instance.Regtest;

			CryptoCode = "BTC";
			nodeDownloadData = NodeDownloadData.Bitcoin.v0_17_0;
			Network = NBitcoin.Network.RegTest;
		}
	}
}
