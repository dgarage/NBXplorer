using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using NBXplorer.JsonConverters;
using NBitcoin;

namespace NBXplorer.Models
{
	public class GenerateWalletRequest
	{
		public int AccountNumber { get; set; }		
		public string ExistingMnemonic { get; set; }
		[JsonConverter(typeof(NBXplorer.JsonConverters.WordlistJsonConverter))]
		public NBitcoin.Wordlist WordList { get; set; }
		[JsonConverter(typeof(NBXplorer.JsonConverters.WordcountJsonConverter))]
		public NBitcoin.WordCount? WordCount { get; set; }
		[JsonConverter(typeof(NBXplorer.JsonConverters.ScriptPubKeyTypeConverter))]
		public NBitcoin.ScriptPubKeyType? ScriptPubKeyType { get; set; }
		public string Passphrase { get; set; }
		public bool ImportKeysToRPC { get; set; }
		public bool SavePrivateKeys { get; set; }
		public Dictionary<string, string> AdditionalOptions { get; set; }
	}
}
