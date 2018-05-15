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
	public class MultisigAndMultisigDerivationStrategy : DerivationStrategyBase
	{
		internal MultisigDerivationStrategy Multisig1
		{
			get; set;
		}
		internal MultisigDerivationStrategy Multisig2
		{
			get; set;
		}
		public bool LexicographicOrder
		{
			get; set;
		}

		protected override string StringValue
		{
			get
			{
				StringBuilder builder = new StringBuilder();
				builder.Append(Multisig1.ToString() + "-and-" + Multisig2.ToString());
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

		internal MultisigAndMultisigDerivationStrategy(MultisigDerivationStrategy multisig1, MultisigDerivationStrategy multisig2, bool isLegacy)
		{
			Multisig1 = Clone(multisig1);
			Multisig2 = Clone(multisig2);
			LexicographicOrder = true;
			IsLegacy = isLegacy;
		}

		static MultisigDerivationStrategy Clone(MultisigDerivationStrategy multisig)
		{
			return new MultisigDerivationStrategy(multisig.RequiredSignatures, multisig.Keys, false) { LexicographicOrder = true };
		}

		public bool IsLegacy
		{
			get; private set;
		}

		public override Derivation Derive(KeyPath keyPath)
		{
			var pubKeys1 = this.Multisig1.Keys.Select(s => s.ExtPubKey.Derive(keyPath).PubKey).ToArray();
			if(LexicographicOrder)
			{
				Array.Sort(pubKeys1, MultisigDerivationStrategy.LexicographicComparer);
			}

			var pubKeys2 = this.Multisig2.Keys.Select(s => s.ExtPubKey.Derive(keyPath).PubKey).ToArray();
			if(LexicographicOrder)
			{
				Array.Sort(pubKeys2, MultisigDerivationStrategy.LexicographicComparer);
			}
			List<Op> ops = new List<Op>();
			ops.Add(Op.GetPushOp(Multisig1.RequiredSignatures));
			foreach(var keys in pubKeys1)
			{
				ops.Add(Op.GetPushOp(keys.ToBytes()));
			}
			ops.Add(Op.GetPushOp(pubKeys1.Length));
			ops.Add(OpcodeType.OP_CHECKMULTISIGVERIFY);
			ops.Add(Op.GetPushOp(Multisig2.RequiredSignatures));
			foreach(var keys in pubKeys2)
			{
				ops.Add(Op.GetPushOp(keys.ToBytes()));
			}
			ops.Add(Op.GetPushOp(pubKeys2.Length));
			ops.Add(OpcodeType.OP_CHECKMULTISIG);

			return new Derivation() { ScriptPubKey = new Script(ops.ToList()) };
		}

		public override DerivationStrategyBase GetLineFor(KeyPath keyPath)
		{
			return new MultisigAndMultisigDerivationStrategy((MultisigDerivationStrategy)Multisig1.GetLineFor(keyPath), (MultisigDerivationStrategy)Multisig2.GetLineFor(keyPath), IsLegacy)
			{
				LexicographicOrder = LexicographicOrder
			};
		}
	}
}
