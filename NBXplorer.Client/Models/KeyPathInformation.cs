using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class KeyPathInformation
	{
		public KeyPathInformation()
		{

		}
		public KeyPathInformation(KeyPath keyPath, DerivationStrategyBase derivationStrategy)
		{
			var derivation = derivationStrategy.Derive(keyPath);
			ScriptPubKey = derivation.ScriptPubKey;
			Redeem = derivation.Redeem;
			TrackedSource = new DerivationSchemeTrackedSource(derivationStrategy);
			DerivationStrategy = derivationStrategy;
			Feature = DerivationStrategyBase.GetFeature(keyPath);
			KeyPath = keyPath;
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
		public string Address { get; set; }
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public Script Redeem
		{
			get; set;
		}
		public int GetIndex()
		{
			return (int)KeyPath.Indexes[KeyPath.Indexes.Length - 1];
		}
	}
}