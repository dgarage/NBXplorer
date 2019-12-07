using NBitcoin;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NBXplorer.DerivationStrategy
{
	public class DerivationStrategyOptions
	{

		public ScriptPubKeyType ScriptPubKeyType
		{
			get
			{
				if (AdditionalOptions.TryGetValue("legacy", out var legacy) && legacy)
				{
					return ScriptPubKeyType.Legacy;
				}
				if (AdditionalOptions.TryGetValue("p2sh", out var p2sh) && p2sh)
				{
					return ScriptPubKeyType.SegwitP2SH;
				}
				return ScriptPubKeyType.Segwit;
			}
			set
			{
				switch (value)
				{
					case ScriptPubKeyType.Legacy:
						AdditionalOptions.AddOrReplace("legacy", true);
						AdditionalOptions.AddOrReplace("p2sh", false);
						break;
					case ScriptPubKeyType.Segwit:
						AdditionalOptions.AddOrReplace("legacy", false);
						AdditionalOptions.AddOrReplace("p2sh", false);
						break;
					case ScriptPubKeyType.SegwitP2SH:
						AdditionalOptions.AddOrReplace("legacy", false);
						AdditionalOptions.AddOrReplace("p2sh", true);
						break;
					default:
						throw new ArgumentOutOfRangeException(nameof(value), value, null);
				}
			}
		}

		public Dictionary<string,bool> AdditionalOptions { get; set; } = new Dictionary<string, bool>();

		/// <summary>
		/// If true, in case of multisig, do not reorder the public keys of an address lexicographically (default: false)
		/// </summary>
		public bool KeepOrder
		{
			get => AdditionalOptions.TryGetValue("keeporder", out var keeporder) && keeporder;
			set => AdditionalOptions.AddOrReplace("keeporder", value);
		}
	}
	public class DerivationStrategyFactory
	{

		private readonly Network _Network;
		public Network Network
		{
			get
			{
				return _Network;
			}
		}
		public DerivationStrategyFactory(Network network)
		{
			if(network == null)
				throw new ArgumentNullException(nameof(network));
			_Network = network;
		}

		readonly Regex MultiSigRegex = new Regex("^([0-9]{1,2})-of(-[A-Za-z0-9]+)+$");
		public DerivationStrategyBase Parse(string str)
		{
			var strategy = ParseCore(str);
			return strategy;
		}

		private DerivationStrategyBase ParseCore(string str)
		{
			var additionalOptions = new Dictionary<string,bool>();
			ReadOptions(ref str, ref additionalOptions);

			additionalOptions.TryGetValue("legacy", out var legacy);
			additionalOptions.TryGetValue("p2sh", out var p2sh);
			additionalOptions.TryGetValue("keeporder", out var keepOrder);

			if(!legacy && !_Network.Consensus.SupportSegwit)
				throw new FormatException("Segwit is not supported");

			var options = new DerivationStrategyOptions()
			{
				KeepOrder = keepOrder,
				ScriptPubKeyType = legacy ? ScriptPubKeyType.Legacy :
									p2sh ? ScriptPubKeyType.SegwitP2SH :
									ScriptPubKeyType.Segwit,
				AdditionalOptions = additionalOptions
		};
			var match = MultiSigRegex.Match(str);
			if(match.Success)
			{
				var sigCount = int.Parse(match.Groups[1].Value);
				var pubKeys = match.Groups
									.OfType<Group>()
									.Skip(2)
									.SelectMany(g => g.Captures.OfType<Capture>())
									.Select(g => new BitcoinExtPubKey(g.Value.Substring(1), Network))
									.ToArray();
				return CreateMultiSigDerivationStrategy(pubKeys, sigCount, options);
			}
			else
			{
				var key = _Network.Parse<BitcoinExtPubKey>(str);
				return CreateDirectDerivationStrategy(key, options);
			}
		}

		/// <summary>
		/// Create a single signature derivation strategy from public key
		/// </summary>
		/// <param name="publicKey">The public key of the wallet</param>
		/// <param name="options">Derivation options</param>
		/// <returns></returns>
		public DerivationStrategyBase CreateDirectDerivationStrategy(ExtPubKey publicKey, DerivationStrategyOptions options = null)
		{
			return CreateDirectDerivationStrategy(publicKey.GetWif(Network), options);
		}

		/// <summary>
		/// Create a single signature derivation strategy from public key
		/// </summary>
		/// <param name="publicKey">The public key of the wallet</param>
		/// <param name="options">Derivation options</param>
		/// <returns></returns>
		public DerivationStrategyBase CreateDirectDerivationStrategy(BitcoinExtPubKey publicKey, DerivationStrategyOptions options = null)
		{
			options = options ?? new DerivationStrategyOptions();
			DerivationStrategyBase strategy = new DirectDerivationStrategy(publicKey, options);
			if(options.ScriptPubKeyType != ScriptPubKeyType.Legacy && !_Network.Consensus.SupportSegwit)
				throw new InvalidOperationException("This crypto currency does not support segwit");

			if(options.ScriptPubKeyType == ScriptPubKeyType.SegwitP2SH)
			{
				strategy = new P2SHDerivationStrategy(strategy, false, options);
			}
			return strategy;
		}

		/// <summary>
		/// Create a multisig derivation strategy from public keys
		/// </summary>
		/// <param name="pubKeys">The public keys belonging to the multi sig</param>
		/// <param name="sigCount">The number of required signature</param>
		/// <param name="options">Derivation options</param>
		/// <returns>A multisig derivation strategy</returns>
		public DerivationStrategyBase CreateMultiSigDerivationStrategy(ExtPubKey[] pubKeys, int sigCount, DerivationStrategyOptions options = null)
		{
			return CreateMultiSigDerivationStrategy(pubKeys.Select(p => p.GetWif(Network)).ToArray(), sigCount, options);
		}

		/// <summary>
		/// Create a multisig derivation strategy from public keys
		/// </summary>
		/// <param name="pubKeys">The public keys belonging to the multi sig</param>
		/// <param name="sigCount">The number of required signature</param>
		/// <param name="options">Derivation options</param>
		/// <returns>A multisig derivation strategy</returns>
		public DerivationStrategyBase CreateMultiSigDerivationStrategy(BitcoinExtPubKey[] pubKeys, int sigCount, DerivationStrategyOptions options = null)
		{
			options = options ?? new DerivationStrategyOptions();
			DerivationStrategyBase derivationStrategy =
				new MultisigDerivationStrategy(sigCount, pubKeys.ToArray(), options);
			if(options.ScriptPubKeyType == ScriptPubKeyType.Legacy)
				return new P2SHDerivationStrategy(derivationStrategy, false, options);

			if(!_Network.Consensus.SupportSegwit)
				throw new InvalidOperationException("This crypto currency does not support segwit");
			derivationStrategy = new P2WSHDerivationStrategy(derivationStrategy, options);
			if(options.ScriptPubKeyType == ScriptPubKeyType.SegwitP2SH)
			{
				derivationStrategy = new P2SHDerivationStrategy(derivationStrategy, true, options);
			}
			return derivationStrategy;
		}

		private void ReadBool(ref string str, string attribute, ref bool value)
		{
			value = str.Contains($"[{attribute}]");
			if(value)
			{
				str = str.Replace($"[{attribute}]", string.Empty);
				str = str.Replace("--", "-");
				if(str.EndsWith("-"))
					str = str.Substring(0, str.Length - 1);
			}
		}
		
		private void ReadOptions(ref string str, ref Dictionary<string, bool> additionalOptions)
		{
			foreach (Match match in Regex.Matches(str, @"-\[.+\]"))
			{
				var key = match.Value.Substring(1)
					.Replace("[", string.Empty)
					.Replace("]", string.Empty);

				var value = false;
				ReadBool(ref str, key, ref value);
				additionalOptions.AddOrReplace(key, value);
			}
		}
	}
}
