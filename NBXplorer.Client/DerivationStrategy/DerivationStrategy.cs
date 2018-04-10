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

		/// <summary>
		/// If true, use P2SH (default: false)
		/// </summary>
		public bool P2SH
		{
			get; set;
		}

		/// <summary>
		/// If false, use segwit (default: false)
		/// </summary>
		public bool Legacy
		{
			get; set;
		}

		/// <summary>
		/// If true, in case of multisig, do not reorder the public keys of an address lexicographically (default: false)
		/// </summary>
		public bool KeepOrder
		{
			get; set;
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
		static DirectDerivationStrategy DummyPubKey = new DirectDerivationStrategy(new ExtKey().Neuter().GetWif(Network.RegTest)) { Segwit = false };
		public DerivationStrategyBase Parse(string str)
		{
			var strategy = ParseCore(str);
			return strategy;
		}

		private DerivationStrategyBase ParseCore(string str)
		{
			bool legacy = false;
			ReadBool(ref str, "legacy", ref legacy);

			bool p2sh = false;
			ReadBool(ref str, "p2sh", ref p2sh);

			bool keepOrder = false;
			ReadBool(ref str, "keeporder", ref keepOrder);

			if(!legacy && !_Network.Consensus.SupportSegwit)
				throw new FormatException("Segwit is not supported");

			var options = new DerivationStrategyOptions()
			{
				KeepOrder = keepOrder,
				Legacy = legacy,
				P2SH = p2sh
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
			DerivationStrategyBase strategy = new DirectDerivationStrategy(publicKey) { Segwit = !options.Legacy };
			if(!options.Legacy && !_Network.Consensus.SupportSegwit)
				throw new InvalidOperationException("This crypto currency does not support segwit");

			if(options.P2SH && !options.Legacy)
			{
				strategy = new P2SHDerivationStrategy(strategy, true);
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
			DerivationStrategyBase derivationStrategy = new MultisigDerivationStrategy(sigCount, pubKeys.ToArray(), options.Legacy)
			{
				LexicographicOrder = !options.KeepOrder
			};
			if(options.Legacy)
				return new P2SHDerivationStrategy(derivationStrategy, false);

			if(!_Network.Consensus.SupportSegwit)
				throw new InvalidOperationException("This crypto currency does not support segwit");
			derivationStrategy = new P2WSHDerivationStrategy(derivationStrategy);
			if(options.P2SH)
			{
				derivationStrategy = new P2SHDerivationStrategy(derivationStrategy, true);
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
	}
}
