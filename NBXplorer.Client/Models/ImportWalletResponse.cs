using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;

namespace NBXplorer.Models
{
	public class ImportWalletResponse
	{
		public BitcoinExtKey MasterHDKey { get; set; }
		public BitcoinExtKey AccountHDKey { get; set; }
		[JsonConverter(typeof(NBitcoin.JsonConverters.KeyPathJsonConverter))]
		public NBitcoin.RootedKeyPath AccountKeyPath { get; set; }
		public DerivationStrategyBase DerivationScheme { get; set; }
	}
}