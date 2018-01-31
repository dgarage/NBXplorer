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
		public bool LexicographicOrder
		{
			get; set;
		}

		public int RequiredSignatures
		{
			get; set;
		}

		static readonly Comparer<PubKey> LexicographicComparer = Comparer<PubKey>.Create((a, b) => Comparer<string>.Default.Compare(a?.ToHex(), b?.ToHex()));

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

		private void WriteBytes(MemoryStream ms, byte[] v)
		{
			ms.Write(v, 0, v.Length);
		}

		public override Derivation Derive(KeyPath keyPath)
		{
			var pubKeys = this.Keys.Select(s => s.ExtPubKey.Derive(keyPath).PubKey).ToArray();
			if(LexicographicOrder)
			{
				Array.Sort(pubKeys, LexicographicComparer);
			}
			var redeem = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(RequiredSignatures, pubKeys);
			return new Derivation() { ScriptPubKey = redeem };
		}

		public override DerivationStrategyBase GetLineFor(KeyPath keyPath)
		{
			return new MultisigDerivationStrategy(RequiredSignatures, Keys.Select(k => k.ExtPubKey.Derive(keyPath).GetWif(k.Network)).ToArray())
			{
				LexicographicOrder = LexicographicOrder
			};
		}
	}
}
