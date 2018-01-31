using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using NBitcoin.Crypto;

namespace NBXplorer.DerivationStrategy
{
	public class DirectDerivationStrategy : DerivationStrategyBase
	{
		BitcoinExtPubKey _Root;

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

		protected override string StringValue
		{
			get
			{
				StringBuilder builder = new StringBuilder();
				builder.Append(_Root.ToString());
				if(!Segwit)
				{
					builder.Append("-[legacy]");
				}
				return builder.ToString();
			}
		}

		public DirectDerivationStrategy(BitcoinExtPubKey root)
		{
			if(root == null)
				throw new ArgumentNullException(nameof(root));
			_Root = root;
		}
		public override Derivation Derive(KeyPath keyPath)
		{
			var pubKey = _Root.ExtPubKey.Derive(keyPath).PubKey;
			return new Derivation() { ScriptPubKey = Segwit ? pubKey.WitHash.ScriptPubKey : pubKey.Hash.ScriptPubKey };
		}

		public override DerivationStrategyBase GetLineFor(KeyPath keyPath)
		{
			return new DirectDerivationStrategy(_Root.ExtPubKey.Derive(keyPath).GetWif(_Root.Network)) { Segwit = Segwit };
		}
	}
}
