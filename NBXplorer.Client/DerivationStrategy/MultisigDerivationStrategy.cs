using NBitcoin;
using System.Linq;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using NBitcoin.Crypto;

namespace NBXplorer.DerivationStrategy
{
	public class MultisigDerivationStrategy : DerivationStrategyBase
	{
		class LineStrategy : DerivationStrategyLine
		{
			private MultisigDerivationStrategy up;
			private ExtPubKey[] rootDerivations;

			public LineStrategy(MultisigDerivationStrategy up, bool change)
			{
				this.up = up;
				Path = new KeyPath(change ? "1" : "0");
				rootDerivations = up.Keys.Select(k => k.ExtPubKey.Derive(Path)).ToArray();
			}

			public KeyPath Path
			{
				get; set;
			}

			readonly Comparer<PubKey> LexicographicComparer = Comparer<PubKey>.Create((a, b) => Comparer<string>.Default.Compare(a?.ToHex(), b?.ToHex()));
			public Derivation Derive(uint i)
			{
				var pubKeys = rootDerivations.Select(s => s.Derive(i).PubKey).ToArray();
				if(up.LexicographicOrder)
				{
					Array.Sort(pubKeys, LexicographicComparer);
				}
				var redeem = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(up.RequiredSignatures, pubKeys);
				return new Derivation() { ScriptPubKey = redeem };
			}
		}
		public bool LexicographicOrder
		{
			get; set;
		}

		public int RequiredSignatures
		{
			get; set;
		}

		public BitcoinExtPubKey[] Keys
		{
			get; set;
		}
		internal MultisigDerivationStrategy(int reqSignature, BitcoinExtPubKey[] keys)
		{
			Keys = keys;
			RequiredSignatures = RequiredSignatures;
			LexicographicOrder = true;
		}

		public override IEnumerable<DerivationStrategyLine> GetLines()
		{
			yield return new LineStrategy(this, false);
			yield return new LineStrategy(this, true);
		}

		private void WriteBytes(MemoryStream ms, byte[] v)
		{
			ms.Write(v, 0, v.Length);
		}

		public override DerivationStrategyLine GetLineFor(DerivationFeature feature)
		{
			if(feature == DerivationFeature.Change)
				return new LineStrategy(this, true);
			if(feature == DerivationFeature.Deposit)
				return new LineStrategy(this, false);
			throw new NotSupportedException();
		}
	}
}
