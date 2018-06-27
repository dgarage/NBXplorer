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
	public class MultisigPlusOneDerivationStrategy : DerivationStrategyBase
	{
		internal MultisigDerivationStrategy Multisig
		{
			get; set;
		}
		internal DirectDerivationStrategy One
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
				builder.Append(Multisig.ToString() + "-and-" + One.ToString());
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

		internal MultisigPlusOneDerivationStrategy(MultisigDerivationStrategy multisig, DirectDerivationStrategy one, bool isLegacy)
		{
			Multisig = Clone(multisig);
			One = Clone(one);
			LexicographicOrder = true;
			IsLegacy = isLegacy;
		}

		private DirectDerivationStrategy Clone(DirectDerivationStrategy one)
		{
			return new DirectDerivationStrategy(one.BitcoinRoot) { Segwit = true };
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

			var pubKeys = this.Multisig.Keys.Select(s => s.ExtPubKey.Derive(keyPath).PubKey).ToArray();
			if(LexicographicOrder)
			{
				Array.Sort(pubKeys, MultisigDerivationStrategy.LexicographicComparer);
			}
			List<Op> ops = new List<Op>();
			ops.Add(Op.GetPushOp(One.Root.Derive(keyPath).PubKey.ToBytes()));
			ops.Add(OpcodeType.OP_CHECKSIGVERIFY);
			ops.Add(Op.GetPushOp(Multisig.RequiredSignatures));
			foreach(var keys in pubKeys)
			{
				ops.Add(Op.GetPushOp(keys.ToBytes()));
			}
			ops.Add(Op.GetPushOp(pubKeys.Length));
			ops.Add(OpcodeType.OP_CHECKMULTISIG);

			return new Derivation() { ScriptPubKey = new Script(ops.ToList()) };
		}

		public override DerivationStrategyBase GetLineFor(KeyPath keyPath)
		{
			return new MultisigPlusOneDerivationStrategy((MultisigDerivationStrategy)Multisig.GetLineFor(keyPath), (DirectDerivationStrategy)One.GetLineFor(keyPath), IsLegacy)
			{
				LexicographicOrder = LexicographicOrder
			};
		}
	}
}
