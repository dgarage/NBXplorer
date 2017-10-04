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
		class P2WSHDerivationLine : DerivationStrategyLine
		{
			private DerivationStrategyLine inner;

			public P2WSHDerivationLine(DerivationStrategyLine inner)
			{
				this.inner = inner;
			}
			public KeyPath Path => inner.Path;

			public Derivation Derive(uint i)
			{
				var derivation = inner.Derive(i);
				return new Derivation()
				{
					ScriptPubKey = derivation.ScriptPubKey.WitHash.ScriptPubKey,
					Redeem = derivation.ScriptPubKey
				};
			}
		}
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

		public override DerivationStrategyLine GetLineFor(DerivationFeature feature)
		{
			var inner = Inner.GetLineFor(feature);
			return new P2WSHDerivationLine(inner);
		}

		public override IEnumerable<DerivationStrategyLine> GetLines()
		{
			return Inner.GetLines().Select(l => new P2WSHDerivationLine(l));
		}
	}
}
