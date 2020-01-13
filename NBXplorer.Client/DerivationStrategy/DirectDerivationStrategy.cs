using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
		}

		protected override string StringValueCore
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

		public DirectDerivationStrategy(BitcoinExtPubKey root, bool segwit, ReadOnlyDictionary<string, bool> additionalOptions = null) : base(additionalOptions)
		{
			if(root == null)
				throw new ArgumentNullException(nameof(root));
			_Root = root;
			Segwit = segwit;
		}
		public override Derivation GetDerivation()
		{
			var pubKey = _Root.ExtPubKey.PubKey;
			return new Derivation() { ScriptPubKey = Segwit ? pubKey.WitHash.ScriptPubKey : pubKey.Hash.ScriptPubKey };
		}

		public override DerivationStrategyBase GetChild(KeyPath keyPath)
		{
			return new DirectDerivationStrategy(_Root.ExtPubKey.Derive(keyPath).GetWif(_Root.Network), Segwit, AdditionalOptions);
		}

		public override IEnumerable<ExtPubKey> GetExtPubKeys()
		{
			yield return _Root.ExtPubKey;
		}
	}
}
