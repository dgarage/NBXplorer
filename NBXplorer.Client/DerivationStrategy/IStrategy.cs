using NBitcoin;
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
	public interface IDerivationStrategy
	{
		IEnumerable<IDerivationStrategyLine> GetLines();
		uint160 GetHash();
		IDerivationStrategyLine GetLineFor(DerivationFeature feature);
	}

	public interface IDerivationStrategyLine
	{
		KeyPath Path
		{
			get;
		}

		Derivation Derive(uint i);
	}
}
