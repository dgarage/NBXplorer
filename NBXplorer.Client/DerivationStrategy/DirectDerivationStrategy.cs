using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using NBitcoin;

namespace NBXplorer.DerivationStrategy
{
	public class DirectDerivationStrategy : StandardDerivationStrategyBase
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

		protected internal override string StringValueCore
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

		public DirectDerivationStrategy(BitcoinExtPubKey root, bool segwit, ReadOnlyDictionary<string, string> additionalOptions = null) : base(additionalOptions)
		{
			if(root == null)
				throw new ArgumentNullException(nameof(root));
			_Root = root;
			Segwit = segwit;
		}

		public override Derivation GetDerivation(KeyPath keyPath)
		{
			var pubKey = _Root.ExtPubKey.Derive(keyPath).PubKey;
			return new Derivation(Segwit ? pubKey.WitHash.ScriptPubKey : pubKey.Hash.ScriptPubKey);
		}

		public override IEnumerable<ExtPubKey> GetExtPubKeys()
		{
			yield return _Root.ExtPubKey;
		}
	}
}
