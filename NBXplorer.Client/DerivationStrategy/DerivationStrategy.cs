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
			strategy.StringValue = str;
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
				DerivationStrategyBase derivationStrategy = new MultisigDerivationStrategy(sigCount, pubKeys)
				{
					LexicographicOrder = !keepOrder
				};
				if(legacy)
					return new P2SHDerivationStrategy(derivationStrategy);

				derivationStrategy = new P2WSHDerivationStrategy(derivationStrategy);
				if(p2sh)
				{
					derivationStrategy = new P2SHDerivationStrategy(derivationStrategy);
				}
				return derivationStrategy;
			}
			else
			{
				var key = _Network.Parse<BitcoinExtPubKey>(str);
				DerivationStrategyBase strategy = new DirectDerivationStrategy(key) { Segwit = !legacy };
				if(p2sh && !legacy)
				{
					strategy = new P2SHDerivationStrategy(strategy);
				}
				return strategy;
			}
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
