using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using NBitcoin;

namespace NBXplorer.DerivationStrategy
{
	public class TaprootDerivationStrategy : StandardDerivationStrategyBase
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
		public override Derivation GetDerivation(KeyPath keyPath)
		{
#if NO_SPAN
			throw new NotSupportedException("Deriving taproot address is not supported on this platform.");
#else
			var pubKey = _Root.ExtPubKey.Derive(keyPath).PubKey.GetTaprootFullPubKey();
			return new Derivation(pubKey.ScriptPubKey);
#endif
		}

		public override IEnumerable<ExtPubKey> GetExtPubKeys()
		{
			yield return _Root.ExtPubKey;
		}
	}
}
