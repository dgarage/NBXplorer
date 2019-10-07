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
		public bool AddSuffix { get; }

		internal P2SHDerivationStrategy(DerivationStrategyBase inner, bool addSuffix)
		{
			if(inner == null)
				throw new ArgumentNullException(nameof(inner));
			Inner = inner;
			this.AddSuffix = addSuffix;
		}

		public DerivationStrategyBase Inner
		{
			get; set;
		}

		protected override string StringValue
		{
			get
			{
				if(AddSuffix)
					return Inner.ToString() + "-[p2sh]";
				return Inner.ToString();
			}
		}

		public override Derivation GetDerivation()
		{
			var derivation = Inner.GetDerivation();
			return new Derivation()
			{
				ScriptPubKey = derivation.ScriptPubKey.Hash.ScriptPubKey,
				Redeem = derivation.Redeem ?? derivation.ScriptPubKey
			};
		}

		public override IEnumerable<ExtPubKey> GetExtPubKeys()
		{
			return Inner.GetExtPubKeys();
		}

		public override DerivationStrategyBase GetChild(KeyPath keyPath)
		{
			return new P2SHDerivationStrategy(Inner.GetChild(keyPath), AddSuffix);
		}
	}
}
