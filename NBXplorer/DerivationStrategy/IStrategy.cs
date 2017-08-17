using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.DerivationStrategy
{
	public interface IDerivationStrategy
	{
		IEnumerable<IDerivationStrategyLine> GetLines();
		uint160 GetHash();
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
