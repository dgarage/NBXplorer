using NBXplorer.DerivationStrategy;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using NBitcoin.Crypto;

namespace NBXplorer.DerivationStrategy
{
	public class P2WSHDerivationStrategy : DerivationStrategyBase
	{
		internal P2WSHDerivationStrategy(DerivationStrategyBase inner)
		{
			if(inner == null)
				throw new ArgumentNullException(nameof(inner));
			Inner = inner;
		}

		public DerivationStrategyBase Inner
		{
			get; set;
		}

		protected override string StringValue => Inner.ToString();

		public override Derivation Derive(KeyPath keyPath)
		{
			var derivation = Inner.Derive(keyPath);
			return new Derivation()
			{
				ScriptPubKey = derivation.ScriptPubKey.WitHash.ScriptPubKey,
				Redeem = derivation.ScriptPubKey
			};
		}

		public override DerivationStrategyBase GetLineFor(KeyPath keyPath)
		{
			return new P2WSHDerivationStrategy(Inner.GetLineFor(keyPath));
		}
	}
}
