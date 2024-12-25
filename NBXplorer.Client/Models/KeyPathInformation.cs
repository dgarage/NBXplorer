using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;

namespace NBXplorer.Models
{
	public class KeyPathInformation
	{
		public TrackedSource TrackedSource { get; set; }
		[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
		public DerivationFeature Feature
		{
			get; set;
		}
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public DerivationStrategyBase DerivationStrategy
		{
			get; set;
		}
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public KeyPath KeyPath
		{
			get; set;
		}
		public Script ScriptPubKey
		{
			get; set;
		}
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public BitcoinAddress Address { get; set; }
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public Script Redeem
		{
			get; set;
		}
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public int? Index { get; set; }
	}
}
