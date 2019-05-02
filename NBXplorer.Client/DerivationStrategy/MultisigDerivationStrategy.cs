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

		internal static readonly Comparer<PubKey> LexicographicComparer = Comparer<PubKey>.Create((a, b) => Comparer<string>.Default.Compare(a?.ToHex(), b?.ToHex()));

		public BitcoinExtPubKey[] Keys
		{
			get; set;
		}

		protected override string StringValue
		{
			get
			{
				StringBuilder builder = new StringBuilder();
				builder.Append(RequiredSignatures);
				builder.Append("-of-");
				builder.Append(string.Join("-", Keys.Select(k => k.ToString()).ToArray()));
				if(IsLegacy)
				{
					builder.Append("-[legacy]");
				}
				if(!LexicographicOrder)
				{
					builder.Append("-[keeporder]");
				}
				return builder.ToString();
			}
		}

		internal MultisigDerivationStrategy(int reqSignature, BitcoinExtPubKey[] keys, bool isLegacy)
		{
			Keys = keys;
			RequiredSignatures = reqSignature;
			LexicographicOrder = true;
			IsLegacy = isLegacy;
		}

		public bool IsLegacy
		{
			get; private set;
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
			return new MultisigDerivationStrategy(RequiredSignatures, Keys.Select(k => k.ExtPubKey.Derive(keyPath).GetWif(k.Network)).ToArray(), IsLegacy)
			{
				LexicographicOrder = LexicographicOrder
			};
		}

		public override IEnumerable<ExtPubKey> GetExtPubKeys()
		{
			return Keys.Select(k => k.ExtPubKey);
		}
	}
}
