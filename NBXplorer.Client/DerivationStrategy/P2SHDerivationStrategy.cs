using NBXplorer.DerivationStrategy;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using NBitcoin.Crypto;

namespace NBXplorer.DerivationStrategy
{
	public class P2SHDerivationStrategy : DerivationStrategyBase
	{
		internal P2SHDerivationStrategy(DerivationStrategyBase inner)
		{
			if(inner == null)
				throw new ArgumentNullException(nameof(inner));
			Inner = inner;
		}

		public DerivationStrategyBase Inner
		{
			get; set;
		}
		public override Derivation Derive(KeyPath keyPath)
		{
			var derivation = Inner.Derive(keyPath);
			return new Derivation()
			{
				ScriptPubKey = derivation.ScriptPubKey.Hash.ScriptPubKey,
				Redeem = derivation.Redeem ?? derivation.ScriptPubKey
			};
		}

		public override DerivationStrategyBase GetLineFor(KeyPath keyPath)
		{
			return new P2SHDerivationStrategy(Inner.GetLineFor(keyPath));
		}
	}
}
