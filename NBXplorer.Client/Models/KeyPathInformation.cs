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
		public KeyPathInformation(DerivationFeature feature, KeyPath keyPath, DerivationStrategyBase derivationStrategy, NBXplorerNetwork network)
		{
			var derivation = derivationStrategy.GetDerivation(keyPath);
			ScriptPubKey = derivation.ScriptPubKey;
			Redeem = derivation.Redeem;
			TrackedSource = new DerivationSchemeTrackedSource(derivationStrategy);
			DerivationStrategy = derivationStrategy;
			Feature = feature;
			KeyPath = keyPath;
			Address = network.CreateAddress(derivationStrategy, keyPath, ScriptPubKey);
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
		public int GetIndex()
		{
			return (int)KeyPath.Indexes[KeyPath.Indexes.Length - 1];
		}
		
		public virtual KeyPathInformation AddAddress(Network network)
		{
			if(Address == null)
			{
				Address = ScriptPubKey.GetDestinationAddress(network).ToString();
			}
			return this;
		}
	}
}
