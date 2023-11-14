using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;

namespace NBXplorer.Models
{
	public class KeyPathInformation
	{
		public KeyPathInformation()
		{

		}
		
		public KeyPathInformation(Derivation derivation, DerivationSchemeTrackedSource derivationStrategy, DerivationFeature feature, KeyPath keyPath, NBXplorerNetwork network)
		{
			ScriptPubKey = derivation.ScriptPubKey;
			Redeem = derivation.Redeem;
			TrackedSource = derivationStrategy;
			DerivationStrategy = derivationStrategy.DerivationStrategy;
			Feature = feature;
			KeyPath = keyPath;
			Address =  network.CreateAddress(derivationStrategy.DerivationStrategy, keyPath, ScriptPubKey);
		}
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
		public int GetIndex(KeyPathTemplates keyPathTemplates)
		{
			return (int)keyPathTemplates.GetKeyPathTemplate(Feature).GetIndex(KeyPath);
		}
	}
}
