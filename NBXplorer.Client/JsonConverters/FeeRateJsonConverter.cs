using NBitcoin;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;

namespace NBXplorer.JsonConverters
{
	public class FeeRateJsonConverter : JsonConverter
	{

		public override bool CanConvert(Type objectType)
		{
			return
				typeof(FeeRate).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			if(reader.TokenType == JsonToken.Null)
				return null;
			if(reader.TokenType != JsonToken.Integer)
				return null;

			var value = (long)reader.Value;
			return new FeeRate(Money.Satoshis(value), 1);
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			var feeRate = value as FeeRate;
			if(feeRate != null)
			{
				writer.WriteValue(feeRate.GetFee(1).Satoshi);
			}
		}

	}
}
