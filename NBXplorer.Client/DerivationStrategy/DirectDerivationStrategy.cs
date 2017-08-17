using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using NBitcoin.Crypto;

namespace NBXplorer.DerivationStrategy
{
	public class DirectDerivationStrategy : IDerivationStrategy
	{
		class LineStrategy : IDerivationStrategyLine
		{
			private ExtPubKey rootDerivation;

			public LineStrategy(ExtPubKey root, bool change)
			{
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
				return new Derivation() { ScriptPubKey = pubKey.Hash.ScriptPubKey };
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
		public DirectDerivationStrategy(ExtPubKey root)
		{
			if(root == null)
				throw new ArgumentNullException(nameof(root));
			_Root = root;
		}
		public IEnumerable<IDerivationStrategyLine> GetLines()
		{
			yield return new LineStrategy(_Root, false);
			yield return new LineStrategy(_Root, true);
		}

		public uint160 GetHash()
		{
			return Hashes.Hash160(_Root.PubKey.ToBytes());
		}
	}
}
