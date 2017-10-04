using NBitcoin;
using NBitcoin.Crypto;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.DerivationStrategy
{
	public enum DerivationFeature
	{
		Change,
		Deposit
	}
	public abstract class DerivationStrategyBase
	{
		internal DerivationStrategyBase()
		{

		}
		public abstract IEnumerable<DerivationStrategyLine> GetLines();
		public abstract DerivationStrategyLine GetLineFor(DerivationFeature feature);

		internal string StringValue
		{
			get; set;
		}

		public override string ToString()
		{
			return StringValue;
		}
	}

	public interface DerivationStrategyLine
	{
		KeyPath Path
		{
			get;
		}

		Derivation Derive(uint i);
	}
}
