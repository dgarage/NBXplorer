using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class TransactionMatch
	{
		public TrackedSource TrackedSource { get; set; }

		public DerivationStrategyBase DerivationStrategy
		{
			get; set;
		}

		public Transaction Transaction
		{
			get; set;
		}
		public List<KeyPathInformation> Outputs
		{
			get; set;
		} = new List<KeyPathInformation>();

		public List<KeyPathInformation> Inputs
		{
			get; set;
		} = new List<KeyPathInformation>();
	}
}
