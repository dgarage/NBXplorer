using System;
using System.Collections.Generic;
using System.Text;
using NBXplorer.DerivationStrategy;

namespace NBXplorer.Models
{
	public class NewTransactionEvent
	{
		public DerivationStrategyBase DerivationScheme
		{
			get;
			set;
		}
	}
}
