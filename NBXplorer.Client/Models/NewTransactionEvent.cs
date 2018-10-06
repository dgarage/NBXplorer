using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;

namespace NBXplorer.Models
{
	public class NewTransactionEvent : NewEventBase
	{
		public uint256 BlockId
		{
			get; set;
		}

		public TrackedSource TrackedSource { get; set; }

		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]

		public DerivationStrategyBase DerivationStrategy
		{
			get; set;
		}

		public TransactionResult TransactionData
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

		public TransactionMatch AsMatch()
		{
			return new TransactionMatch() { DerivationStrategy = DerivationStrategy, TrackedSource = TrackedSource, Inputs = Inputs, Outputs = Outputs, Transaction = TransactionData.Transaction };
		}
	}
}
