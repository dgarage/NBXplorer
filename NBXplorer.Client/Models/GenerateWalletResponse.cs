using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class GenerateWalletResponse
	{
		public string Mnemonic { get; set; }
		public string Passphrase { get; set; }
		[JsonConverter(typeof(NBXplorer.JsonConverters.WordlistJsonConverter))]
		public NBitcoin.Wordlist WordList { get; set; }
		[JsonConverter(typeof(NBXplorer.JsonConverters.WordcountJsonConverter))]
		public NBitcoin.WordCount WordCount { get; set; }
		public BitcoinExtKey MasterHDKey { get; set; }
		public BitcoinExtKey AccountHDKey { get; set; }
		[JsonConverter(typeof(NBitcoin.JsonConverters.KeyPathJsonConverter))]
		public NBitcoin.RootedKeyPath AccountKeyPath { get; set; }
		public DerivationStrategyBase DerivationScheme { get; set; }
	}
}
