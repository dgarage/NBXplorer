using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class TrackWalletRequest
	{
		public TrackDerivationOption[] DerivationOptions { get; set; }
		public bool Wait { get; set; } = false;
	}

	public class TrackDerivationOption
	{
		[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
		public DerivationFeature? Feature { get; set; }
		public int? MinAddresses { get; set; }
		public int? MaxAddresses { get; set; }
	}
}
