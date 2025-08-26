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
			if (check && !IsValidPolicy(policy, out var error))
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

		// Extract the multipath node from the hdkey
		class MultipathNodeVisitor : MiniscriptVisitor
		{
			private readonly ExtPubKey _target;
			public MiniscriptNode.MultipathNode? Result { get; set; }
			public MultipathNodeVisitor(IHDKey target)
			{
				ArgumentNullException.ThrowIfNull(target);
				_target = Normalize(target);
			}

			private static ExtPubKey Normalize(IHDKey target)
				=> target switch
				{
					BitcoinExtKey extKey => extKey.Neuter().ExtPubKey,
					ExtKey extKey => extKey.Neuter(),
					BitcoinExtPubKey bitcoinExtPubKey => bitcoinExtPubKey.ExtPubKey,
					ExtPubKey a => a, 
					_ => throw new NotSupportedException(target.GetType().ToString())
				};

			public override void Visit(MiniscriptNode node)
			{
				if (Result is not null)
					return;
				if (node is MiniscriptNode.MultipathNode { Target: MiniscriptNode.HDKeyNode hd } mp)
				{
					if (Normalize(hd.Key).Equals(_target))
						Result = mp;
				}
				else
					base.Visit(node);
			}
		}
		
		class MiniscriptScriptPubKey : IHDScriptPubKey
		{
			private readonly PolicyDerivationStrategy _policyDerivationStrategy;
			private readonly MiniscriptNode.MultipathNode _multipathNode;
			private readonly KeyPath _keyPath;

			public MiniscriptScriptPubKey(
				PolicyDerivationStrategy policyDerivationStrategy,
				MiniscriptNode.MultipathNode multipathNode,
				KeyPath? keyPath = null,
				DerivationCache? cache = null)
			{
				_policyDerivationStrategy = policyDerivationStrategy;
				_multipathNode = multipathNode;
				_keyPath = keyPath ?? KeyPath.Empty;
				_cache = cache ?? new();
			}

			private readonly DerivationCache _cache;
			public IHDScriptPubKey? Derive(KeyPath keyPath) =>
				_keyPath.Derive(keyPath) is { Length: <= 2 } kp
					&& (kp.Length == 0 || GetAddressIntent(kp.Indexes[0]) is not null)
					&& !kp.IsHardenedPath
					? new MiniscriptScriptPubKey(_policyDerivationStrategy, _multipathNode, kp, _cache) : null;

			public Script ScriptPubKey
			{
				get
				{
					if (_keyPath is not { Indexes: [var intentIdx, var index], IsHardenedPath: false } 
					    || GetAddressIntent(intentIdx) is not {} intent)
						throw new InvalidOperationException("Invalid keypath (it should be non hardened with two component)");
					var derived = _policyDerivationStrategy.Policy.FullDescriptor.Derive(new(intent, new[] { (int)index })
					{
						DervivationCache = _cache
					});
					return derived[0].Miniscript.ToScripts().ScriptPubKey;
				}
			}

			private AddressIntent? GetAddressIntent(uint intentIdx)
			=> intentIdx == _multipathNode.DepositIndex ? AddressIntent.Deposit :
				intentIdx == _multipathNode.ChangeIndex ? AddressIntent.Change : null;
		}

		public IHDScriptPubKey? GetHDScriptPubKey(IHDKey accountKey)
		{
			ArgumentNullException.ThrowIfNull(accountKey);
			var visitor = new MultipathNodeVisitor(accountKey);
			visitor.Visit(Policy.FullDescriptor.RootNode);
			if (visitor.Result is null)
				return null;
			return new MiniscriptScriptPubKey(this, visitor.Result);
		}
	}
}
#endif