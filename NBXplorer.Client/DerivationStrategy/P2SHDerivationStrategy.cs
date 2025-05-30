using System;
using System.Collections.Generic;
using NBitcoin;

namespace NBXplorer.DerivationStrategy
{
	public class P2SHDerivationStrategy : StandardDerivationStrategyBase
	{
		bool addSuffix;
		internal P2SHDerivationStrategy(StandardDerivationStrategyBase inner, bool addSuffix):base(inner.AdditionalOptions)
		{
			if(inner == null)
				throw new ArgumentNullException(nameof(inner));
			Inner = inner;
			this.addSuffix = addSuffix;
		}

		public StandardDerivationStrategyBase Inner
		{
			get; set;
		}

		protected internal override string StringValueCore
		{
			get
			{
				if(addSuffix)
					return Inner.StringValueCore + "-[p2sh]";
				return Inner.ToString();
			}
		}

		public override IEnumerable<ExtPubKey> GetExtPubKeys()
		{
			return Inner.GetExtPubKeys();
		}

		public override Derivation GetDerivation(KeyPath keyPath)
		{
			var derivation = Inner.GetDerivation(keyPath);
			return new Derivation(
				 derivation.ScriptPubKey.Hash.ScriptPubKey,
				 derivation.Redeem ?? derivation.ScriptPubKey);
		}
	}
}
