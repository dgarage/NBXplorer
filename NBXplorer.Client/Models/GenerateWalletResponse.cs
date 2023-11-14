using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;

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
		public string AccountDescriptor { get; set; }
		public DerivationStrategyBase DerivationScheme { get; set; }

		public Mnemonic GetMnemonic()
		{
			return new Mnemonic(Mnemonic, WordList);
		}
	}
}
