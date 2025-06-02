using NBitcoin;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Collections.ObjectModel;

namespace NBXplorer.DerivationStrategy
{
	public class MultisigDerivationStrategy : StandardDerivationStrategyBase
	{
		public bool LexicographicOrder
		{
			get;
		}

		public int RequiredSignatures
		{
			get;
		}

		static readonly Comparer<PubKey> LexicographicComparer = Comparer<PubKey>.Create((a, b) => Comparer<string>.Default.Compare(a?.ToHex(), b?.ToHex()));

		public ReadOnlyCollection<BitcoinExtPubKey> Keys
		{
			get;
		}

		protected internal override string StringValueCore
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

		internal MultisigDerivationStrategy(int reqSignature, BitcoinExtPubKey[] keys, bool isLegacy, bool lexicographicOrder,
			ReadOnlyDictionary<string, string> additionalOptions) : base(additionalOptions)
		{
			Keys = new ReadOnlyCollection<BitcoinExtPubKey>(keys);
			RequiredSignatures = reqSignature;
			LexicographicOrder = lexicographicOrder;
			IsLegacy = isLegacy;
		}

		public bool IsLegacy
		{
			get;
		}

		public override Derivation GetDerivation(KeyPath keyPath)
		{
			var pubKeys = new PubKey[this.Keys.Count];
			Parallel.For(0, pubKeys.Length, i =>
			{
				pubKeys[i] = this.Keys[i].ExtPubKey.Derive(keyPath).PubKey;
			});
			if(LexicographicOrder)
			{
				Array.Sort(pubKeys, LexicographicComparer);
			}
			var redeem = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(RequiredSignatures, pubKeys);
			return new Derivation(redeem);
		}

		public override IEnumerable<ExtPubKey> GetExtPubKeys()
		{
			return Keys.Select(k => k.ExtPubKey);
		}
	}
}
