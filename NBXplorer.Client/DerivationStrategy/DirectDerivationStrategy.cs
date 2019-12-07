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

		public bool Segwit => DerivationStrategyOptions.ScriptPubKeyType != ScriptPubKeyType.Segwit;

		protected override string StringValue
		{
			get
			{
				StringBuilder builder = new StringBuilder();
				builder.Append(_Root);
				builder.Append(GetSuffixOptionsString());
				return builder.ToString();
			}
		}

		public DirectDerivationStrategy(BitcoinExtPubKey root, DerivationStrategyOptions options) : base(options)
		{
			if(root == null)
				throw new ArgumentNullException(nameof(root));
			_Root = root;
		}
		public override Derivation GetDerivation()
		{
			var pubKey = _Root.ExtPubKey.PubKey;
			return new Derivation() { ScriptPubKey = Segwit ? pubKey.WitHash.ScriptPubKey : pubKey.Hash.ScriptPubKey };
		}

		public override DerivationStrategyBase GetChild(KeyPath keyPath)
		{
			return new DirectDerivationStrategy(_Root.ExtPubKey.Derive(keyPath).GetWif(_Root.Network),
				DerivationStrategyOptions);
		}

		public override IEnumerable<ExtPubKey> GetExtPubKeys()
		{
			yield return _Root.ExtPubKey;
		}
	}
}
