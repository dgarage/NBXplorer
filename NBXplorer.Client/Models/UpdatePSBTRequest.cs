using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace NBXplorer.Models
{
	public class UpdatePSBTRequest
	{
		[JsonProperty("psbt")]
		public PSBT PSBT { get; set; }


		public DerivationStrategyBase DerivationScheme { get; set; }

		/// <summary>
		/// Rebase the hdkey paths (if no rebase, the key paths are relative to the xpub that NBXplorer knows about)
		/// This transform (PubKey0, 0/0, accountFingerprint) by (PubKey0, m/49'/0'/0/0, masterFingerprint) 
		/// </summary>
		public List<PSBTRebaseKeyRules> RebaseKeyPaths { get; set; }
		
		/// <summary>
		/// Attempt setting non_witness_utxo for all inputs even if they are segwit.
		/// </summary>
		public bool AlwaysIncludeNonWitnessUTXO { get; set; }

		/// <summary>
		/// Whether the include the global xpub in the PSBT (default: false)
		/// </summary>
		public bool? IncludeGlobalXPub { get; set; }
	}
	public class UpdatePSBTResponse
	{
		[JsonProperty("psbt")]
		public PSBT PSBT { get; set; }
	}
}
