#nullable enable
#if !NO_RECORD
using NBitcoin;
using NBitcoin.WalletPolicies;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;

namespace NBXplorer.DerivationStrategy
{
	public class PolicyDerivationStrategy : DerivationStrategyBase
	{
		internal static readonly Regex _MaybeMiniscript = new("^(wsh|sh|pkh|tr|wpkh)\\(");
		public static bool TryParse(
			string str,
			Network network,
			[MaybeNullWhen(false)] out PolicyDerivationStrategy strategy)
		{
			strategy = null;
			if (!_MaybeMiniscript.IsMatch(str))
				return false;
			if (!WalletPolicy.TryParse(str, network, out var policy) || !IsValidPolicy(policy, out _))
				return false;
			strategy = new PolicyDerivationStrategy(policy, false);
			return true;
		}

		public static PolicyDerivationStrategy Parse(string str, Network network)
		{
			if (!_MaybeMiniscript.IsMatch(str))
				throw new FormatException("The policy should start by either wsh, sh, pkh, tr, wpkh");
			var policy = WalletPolicy.Parse(str, network);
			if (!IsValidPolicy(policy, out var err))
				throw new FormatException(err);
			return new PolicyDerivationStrategy(policy, false);
		}

		public PolicyDerivationStrategy(WalletPolicy policy) : this(policy, true)
		{
		}

		PolicyDerivationStrategy(WalletPolicy policy, bool check) : base(null)
		{
			if (check && IsValidPolicy(policy, out var error))
				throw new ArgumentException(paramName: nameof(policy), message: error);
			Policy = policy;
		}

		/// <summary>
		/// Check that the policy should have at least one multi path node ([12345678]xpub/**) and no xpriv
		/// </summary>
		/// <param name="policy"></param>
		/// <param name="error"></param>
		/// <returns></returns>
		/// <exception cref="NotImplementedException"></exception>
		public static bool IsValidPolicy(WalletPolicy policy, [MaybeNullWhen(true)] out string error)
		{
			var v = new ValidPolicyVisitor();
			policy.FullDescriptor.Visit(v);
			error = v.Error;
			return error is null;
		}

		class ValidPolicyVisitor : MiniscriptVisitor
		{
			public string? Error {
				get
				{
					if (hasSecretKey)
						return "The policy should not contain any xpriv key";
					if (!hasMultiPathNode)
						return "The policy should contain at least one multi path node ([12345678]xpub/**)";
					return null;
				}
			}
			private bool hasMultiPathNode;
			private bool hasSecretKey;

			public override void Visit(MiniscriptNode node)
			{
				if (node is MiniscriptNode.MultipathNode)
					hasMultiPathNode = true;
				else if (node is MiniscriptNode.HDKeyNode { Key: BitcoinExtKey })
					hasSecretKey = true;
				else
					base.Visit(node);
			}
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