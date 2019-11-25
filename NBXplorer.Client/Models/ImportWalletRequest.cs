using NBitcoin;
using Newtonsoft.Json;

namespace NBXplorer.Models
{
	public class ImportWalletRequest
	{
		[JsonConverter(typeof(NBXplorer.JsonConverters.MnemonicConverter))]
		public Mnemonic Mnemonic { get; set; }
		public string Passphrase { get; set; }
		public int AccountNumber { get; set; } = 0;
		public NBitcoin.ScriptPubKeyType? ScriptPubKeyType { get; set; }
		public bool ImportKeysToRPC { get; set; }
		public bool SavePrivateKeys { get; set; }
	}
}