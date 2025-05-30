#nullable enable
#if !NO_RECORD
using NBitcoin;
using NBitcoin.WalletPolicies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NBXplorer.DerivationStrategy
{
	public class PolicyDerivationStrategy : DerivationStrategyBase
	{
		public PolicyDerivationStrategy(WalletPolicy policy) : base(null)
		{
			Policy = policy;
		}

		public WalletPolicy Policy { get; }
		private readonly DerivationCache cache = new();
		private string? _str;

		protected internal override string StringValueCore => _str ??= Policy.ToString(true);

		public override IEnumerable<ExtPubKey> GetExtPubKeys()
		=> Policy.KeyInformationVector.Select(kv => GetExtPubKey(kv.Key));

		private ExtPubKey GetExtPubKey(IHDKey key)
		=> key switch
		{
			ExtPubKey extPubKey => extPubKey,
			BitcoinExtPubKey bitcoinExtPubKey => bitcoinExtPubKey.ExtPubKey,
			ExtKey extKey => extKey.Neuter(),
			BitcoinExtKey bitcoinExtKey => bitcoinExtKey.ExtKey.Neuter(),
			_ => throw new NotSupportedException($"Unsupported key type: {key.GetType()}")
		};
		public NBXplorer.DerivationStrategy.Derivation GetDerivation(DerivationFeature feature, uint index)
		=> GetDerivation(MiniscriptDerivationLine.ToAddressIntent(feature), index);
		public NBXplorer.DerivationStrategy.Derivation GetDerivation(AddressIntent addressIntent, uint index)
		{
			var derived = Policy.FullDescriptor.Derive(new(addressIntent, [(int)index]) { DervivationCache = cache });
			var scripts = derived[0].Miniscript.ToScripts();
			return new PolicyDerivation(derived[0], scripts.ScriptPubKey, scripts.RedeemScript);
		}

		public override DerivationLine GetLineFor(KeyPathTemplates keyPathTemplates, DerivationFeature feature) => new MiniscriptDerivationLine(this, feature);
	}
}
#endif