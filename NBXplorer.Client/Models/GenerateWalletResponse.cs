using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;

namespace NBXplorer.Models
{
	public class GenerateWalletResponse : ImportWalletResponse
	{
		[JsonConverter(typeof(NBXplorer.JsonConverters.MnemonicConverter))]
		public Mnemonic Mnemonic { get; set; }
		public string Passphrase { get; set; }

		[JsonConverter(typeof(NBXplorer.JsonConverters.WordlistJsonConverter))]
		public NBitcoin.Wordlist WordList { get; set; }

		[JsonConverter(typeof(NBXplorer.JsonConverters.WordcountJsonConverter))]
		public NBitcoin.WordCount WordCount { get; set; }
	}
}
