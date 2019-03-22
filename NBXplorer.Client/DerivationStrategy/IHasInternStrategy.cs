using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.DerivationStrategy
{
	public interface IHasInternStrategy
	{
		DerivationStrategyBase Inner { get;  }
	}
}
