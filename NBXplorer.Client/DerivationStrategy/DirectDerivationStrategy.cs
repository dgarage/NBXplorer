using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using NBitcoin.Crypto;

namespace NBXplorer.DerivationStrategy
{
	public class DirectDerivationStrategy : DerivationStrategyBase
	{
		ExtPubKey _Root;

		public ExtPubKey Root
		{
			get
			{
				return _Root;
			}
		}

		public bool Segwit
		{
			get;
			set;
		}
		internal DirectDerivationStrategy(ExtPubKey root)
		{
			if(root == null)
				throw new ArgumentNullException(nameof(root));
			_Root = root;
		}
		public override Derivation Derive(KeyPath keyPath)
		{
			var pubKey = _Root.Derive(keyPath).PubKey;
			return new Derivation() { ScriptPubKey = Segwit ? pubKey.WitHash.ScriptPubKey : pubKey.Hash.ScriptPubKey };
		}

		public override DerivationStrategyBase GetLineFor(KeyPath keyPath)
		{
			return new DirectDerivationStrategy(_Root.Derive(keyPath)) { Segwit = Segwit };
		}
	}
}
