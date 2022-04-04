using NBitcoin;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NBXplorer.DerivationStrategy
{
	public class DerivationStrategyOptions
	{
		public ScriptPubKeyType ScriptPubKeyType { get; set; }

		/// <summary>
		/// If true, in case of multisig, do not reorder the public keys of an address lexicographically (default: false)
		/// </summary>
		public bool KeepOrder
		{
			get; set;
		}

		public ReadOnlyDictionary<string, string> AdditionalOptions { get; set; }
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
			if (network == null)
				throw new ArgumentNullException(nameof(network));
			_Network = network;
			if (_Network.Consensus.SupportSegwit)
			{
				AuthorizedOptions.Add("p2sh");
			}
			if (_Network.Consensus.SupportTaproot)
			{
				AuthorizedOptions.Add("taproot");
			}
			AuthorizedOptions.Add("keeporder");
			AuthorizedOptions.Add("legacy");
		}

		public HashSet<string> AuthorizedOptions { get; } = new HashSet<string>();

		readonly Regex MultiSigRegex = new Regex("^([0-9]{1,2})-of(-[A-Za-z0-9]+)+$");
		static DirectDerivationStrategy DummyPubKey = new DirectDerivationStrategy(new ExtKey().Neuter().GetWif(Network.RegTest), false);
		public DerivationStrategyBase Parse(string str)
		{
			var strategy = ParseCore(str);
			return strategy;
		}

		private DerivationStrategyBase ParseCore(string str)
		{
			bool legacy = false;
			bool p2sh = false;
			bool keepOrder = false;
			bool taproot = false;
			ScriptPubKeyType type = ScriptPubKeyType.Segwit;

			IDictionary<string, string> optionsDictionary = new Dictionary<string, string>(5);
			foreach (Match optionMatch in _OptionRegex.Matches(str))
			{
				var rawKey = optionMatch.Groups[1].Value.ToLowerInvariant();
				var splitKey = rawKey.Split(new[]{'='}, StringSplitOptions.RemoveEmptyEntries);
				var key = splitKey[0];
				var value = splitKey.Length > 1 ? splitKey[1]: null;
				if (!AuthorizedOptions.Contains(key))
					throw new FormatException($"The option '{key}' is not supported by this network");
				if (!Extensions.TryAdd(optionsDictionary, key, value))
					throw new FormatException($"The option '{key}' is duplicated");
			}
			str = _OptionRegex.Replace(str, string.Empty);
			if (optionsDictionary.Remove("legacy"))
			{
				legacy = true;
				type = ScriptPubKeyType.Legacy;
			}
			if (optionsDictionary.Remove("p2sh"))
			{
				p2sh = true;
				type = ScriptPubKeyType.SegwitP2SH;
			}
			if (optionsDictionary.Remove("keeporder"))
			{
				keepOrder = true;
			}
			if (optionsDictionary.Remove("taproot"))
			{
				taproot = true;
#pragma warning disable CS0618 // Type or member is obsolete
				type = ScriptPubKeyType.TaprootBIP86;
#pragma warning restore CS0618 // Type or member is obsolete
			}
			if (!legacy && !_Network.Consensus.SupportSegwit)
				throw new FormatException("Segwit is not supported you need to specify option '-[legacy]'");

			if (legacy && p2sh)
				throw new FormatException("The option 'legacy' is incompatible with 'p2sh'");

			if (taproot)
			{
				if (!_Network.Consensus.SupportTaproot)
				{
					throw new FormatException("Taproot is not supported, you need to remove option '-[taproot]'");
				}
				else
				{
					if (p2sh)
						throw new FormatException("The option 'taproot' is incompatible with 'p2sh'");
					if (legacy)
						throw new FormatException("The option 'taproot' is incompatible with 'legacy'");
					if (keepOrder)
						throw new FormatException("The option 'taproot' is incompatible with 'keeporder'");
				}
			}

			var options = new DerivationStrategyOptions()
			{
				KeepOrder = keepOrder,
				ScriptPubKeyType = type,
				AdditionalOptions = new ReadOnlyDictionary<string, string>(optionsDictionary)
			};
			var match = MultiSigRegex.Match(str);
			if (match.Success)
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
			DerivationStrategyBase strategy = null;
#pragma warning disable CS0618 // Type or member is obsolete
			if (options.ScriptPubKeyType != ScriptPubKeyType.TaprootBIP86)
#pragma warning restore CS0618 // Type or member is obsolete
			{
				strategy = new DirectDerivationStrategy(publicKey, options.ScriptPubKeyType != ScriptPubKeyType.Legacy, options.AdditionalOptions);
				if (options.ScriptPubKeyType == ScriptPubKeyType.Segwit && !_Network.Consensus.SupportSegwit)
					throw new InvalidOperationException("This crypto currency does not support segwit");

				if (options.ScriptPubKeyType == ScriptPubKeyType.SegwitP2SH)
				{
					strategy = new P2SHDerivationStrategy(strategy, true);
				}
			}
			else
			{
				if (!_Network.Consensus.SupportTaproot)
					throw new InvalidOperationException("This crypto currency does not support taproot");
				strategy = new TaprootDerivationStrategy(publicKey, options.AdditionalOptions);
			}
			return strategy;
		}
		/// <summary>
		/// Create a taproot signature derivation strategy from public key
		/// </summary>
		/// <param name="publicKey">The public key of the wallet</param>
		/// <param name="options">Derivation options</param>
		/// <returns></returns>
		public TaprootDerivationStrategy CreateTaprootDerivationStrategy(BitcoinExtPubKey publicKey, ReadOnlyDictionary<string, string> options = null)
		{
			return new TaprootDerivationStrategy(publicKey, options);
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
			DerivationStrategyBase derivationStrategy = new MultisigDerivationStrategy(sigCount, pubKeys.ToArray(), options.ScriptPubKeyType == ScriptPubKeyType.Legacy, !options.KeepOrder, options.AdditionalOptions);
			if (options.ScriptPubKeyType == ScriptPubKeyType.Legacy)
				return new P2SHDerivationStrategy(derivationStrategy, false);

			if (!_Network.Consensus.SupportSegwit)
				throw new InvalidOperationException("This crypto currency does not support segwit");
			derivationStrategy = new P2WSHDerivationStrategy(derivationStrategy);
			if (options.ScriptPubKeyType == ScriptPubKeyType.SegwitP2SH)
			{
				derivationStrategy = new P2SHDerivationStrategy(derivationStrategy, true);
			}
			return derivationStrategy;
		}

		private void ReadBool(ref string str, string attribute, ref bool value)
		{
			value = str.Contains($"[{attribute}]");
			if (value)
			{
				str = str.Replace($"[{attribute}]", string.Empty);
				str = str.Replace("--", "-");
				if (str.EndsWith("-"))
					str = str.Substring(0, str.Length - 1);
			}
		}

		readonly static Regex _OptionRegex = new Regex(@"-\[([^ \]\-]+)\]");
	}
}
