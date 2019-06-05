using System;
using System.Linq;
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
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public string UnlockId { get; set; }
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

		public List<MatchedOutput> Outputs
		{
			get; set;
		} = new List<MatchedOutput>();

		protected override object[] GetEventIdCore()
		{
			return new object[] { "newtx", BlockId, TransactionData.TransactionHash, TrackedSource };
		}

		[JsonIgnore]
		public override string EventType => "newtransaction";

		public override string ToString()
		{
			var conf = (BlockId == null ? "unconfirmed" : "confirmed");

			string strategy = TrackedSource.ToPrettyString();
			string txId = TransactionData.TransactionHash.ToPrettyString();

			string keyPathSuffix = string.Empty;
			var keyPaths = Outputs.Select(v => v.KeyPath?.ToString()).Where(k => k != null).ToArray();
			if (keyPaths.Length != 0)
			{
				keyPathSuffix = $" ({String.Join(", ", keyPaths)})";
			}
			return $"{CryptoCode}: {strategy} matching {conf} transaction {txId}{keyPathSuffix}";
		}
	}

	public class MatchedOutput
	{
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public KeyPath KeyPath { get; set; }
		public Script ScriptPubKey { get; set; }
		public string Address { get; set; }
		public int Index { get; set; }
		public Money Value { get; set; }
	}
}