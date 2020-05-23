using NBitcoin;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class CreatePSBTResponse
	{
		[JsonProperty("psbt")]
		public PSBT PSBT { get; set; }
		public BitcoinAddress ChangeAddress { get; set; }
		public CreatePSBTSuggestions Suggestions { get; set; }

	}
	public class CreatePSBTSuggestions
	{
		public bool ShouldEnforceLowR { get; set; }
	}
}
