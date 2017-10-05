using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using NBitcoin.Crypto;

namespace NBXplorer.DerivationStrategy
{
	public class DirectDerivationStrategy : DerivationStrategyBase
	{
		class LineStrategy : DerivationStrategyLine
		{
			private ExtPubKey rootDerivation;
			private DirectDerivationStrategy up;
			public LineStrategy(DirectDerivationStrategy up, ExtPubKey root, bool change)
			{
				this.up = up;
				Path = new KeyPath(change ? "1" : "0");
				rootDerivation = root.Derive(Path);
			}

			public KeyPath Path
			{
				get; set;
			}

			public Derivation Derive(uint i)
			{
				var pubKey = rootDerivation.Derive(i).PubKey;
				return new Derivation() { ScriptPubKey = up.Segwit ? pubKey.WitHash.ScriptPubKey : pubKey.Hash.ScriptPubKey };
			}
		}

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

		public override DerivationStrategyLine GetLineFor(DerivationFeature feature)
		{
			if(feature == DerivationFeature.Change)
				return new LineStrategy(this, _Root, true);
			if(feature == DerivationFeature.Deposit)
				return new LineStrategy(this, _Root, false);
			throw new NotSupportedException();
		}
	}
}
