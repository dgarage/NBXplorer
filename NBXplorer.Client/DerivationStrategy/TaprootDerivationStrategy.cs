using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using NBitcoin;
using NBitcoin.Crypto;

namespace NBXplorer.DerivationStrategy
{
	public class TaprootDerivationStrategy : DerivationStrategyBase
	{
		BitcoinExtPubKey _Root;

		public ExtPubKey Root
		{
			get
			{
				return _Root;
			}
		}

		protected internal override string StringValueCore
		{
			get
			{
				StringBuilder builder = new StringBuilder();
				builder.Append(_Root.ToString());
				builder.Append("-[taproot]");
				return builder.ToString();
			}
		}

		public TaprootDerivationStrategy(BitcoinExtPubKey root, ReadOnlyDictionary<string, string> additionalOptions = null) : base(additionalOptions)
		{
			if (root == null)
				throw new ArgumentNullException(nameof(root));
			_Root = root;
		}
		public override Derivation GetDerivation()
		{
#if NO_SPAN
			throw new NotSupportedException("Deriving taproot address is not supported on this platform.");
#else
			var pubKey = _Root.ExtPubKey.PubKey.GetTaprootFullPubKey();
			return new Derivation() { ScriptPubKey = pubKey.ScriptPubKey };
#endif
		}

		public override DerivationStrategyBase GetChild(KeyPath keyPath)
		{
			return new TaprootDerivationStrategy(_Root.ExtPubKey.Derive(keyPath).GetWif(_Root.Network), AdditionalOptions);
		}

		public override IEnumerable<ExtPubKey> GetExtPubKeys()
		{
			yield return _Root.ExtPubKey;
		}
	}
}
