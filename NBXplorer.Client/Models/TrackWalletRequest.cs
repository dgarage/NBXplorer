using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;

namespace NBXplorer.Models
{
	public class TrackWalletRequest
	{
		public TrackDerivationOption[] DerivationOptions { get; set; }
		public bool Wait { get; set; } = false;
		public TrackedSource ParentWallet { get; set; }
	}

	public class TrackDerivationOption
	{
		[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
		public DerivationFeature? Feature { get; set; }
		public int? MinAddresses { get; set; }
		public int? MaxAddresses { get; set; }
	}
}
