using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class GenerateWalletResponse
	{
		[JsonConverter(typeof(NBitcoin.JsonConverters.HDFingerprintJsonConverter))]
		public NBitcoin.HDFingerprint MasterFingerprint { get; set; }
		[JsonConverter(typeof(NBitcoin.JsonConverters.KeyPathJsonConverter))]
		public NBitcoin.KeyPath AccountKeyPath { get; set; }
		public DerivationStrategyBase DerivationStrategy { get; set; }
		public string Mnemonic { get; set; }
		public string Passphrase { get; set; }
		[JsonConverter(typeof(NBXplorer.JsonConverters.WordlistJsonConverter))]
		public NBitcoin.Wordlist WordList { get; set; }
		[JsonConverter(typeof(NBXplorer.JsonConverters.WordcountJsonConverter))]
		public NBitcoin.WordCount WordCount { get; set; }
	}
}
