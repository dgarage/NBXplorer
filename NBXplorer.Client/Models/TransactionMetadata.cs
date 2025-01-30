using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class TransactionMetadata
	{
		[JsonProperty("vsize", DefaultValueHandling = DefaultValueHandling.Ignore)]
		public int? VirtualSize { get; set; }
		[JsonProperty("fees", DefaultValueHandling = DefaultValueHandling.Ignore)]
		[JsonConverter(typeof(NBXplorer.JsonConverters.MoneyJsonConverter))]
		public Money Fees { get; set; }
		[JsonProperty("feeRate", DefaultValueHandling = DefaultValueHandling.Ignore)]
		[JsonConverter(typeof(NBitcoin.JsonConverters.FeeRateJsonConverter))]
		public FeeRate FeeRate { get; set; }
		public static TransactionMetadata Parse(string json) => JsonConvert.DeserializeObject<TransactionMetadata>(json);
		public string ToString(bool indented) => JsonConvert.SerializeObject(this, indented ? Formatting.Indented : Formatting.None);
		public override string ToString() => ToString(true);

		[JsonExtensionData]
		public IDictionary<string, JToken> AdditionalData { get; set; } = new Dictionary<string, JToken>();
	}
}
