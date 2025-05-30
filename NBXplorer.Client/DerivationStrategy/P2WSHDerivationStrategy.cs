using System;
using System.Collections.Generic;
using NBitcoin;

namespace NBXplorer.DerivationStrategy
{
	public class P2WSHDerivationStrategy : StandardDerivationStrategyBase
	{
		internal P2WSHDerivationStrategy(StandardDerivationStrategyBase inner):base(inner.AdditionalOptions)
		{
			if(inner == null)
				throw new ArgumentNullException(nameof(inner));
			Inner = inner;
		}

		public StandardDerivationStrategyBase Inner
		{
			get; set;
		}

		protected internal override string StringValueCore => Inner.ToString();

		public override IEnumerable<ExtPubKey> GetExtPubKeys()
		{
			return Inner.GetExtPubKeys();
		}

		public override Derivation GetDerivation(KeyPath keyPath)
		{
			var redeem = Inner.GetDerivation(keyPath).ScriptPubKey;
			return new Derivation(redeem.WitHash.ScriptPubKey, redeem);
		}
	}
}
