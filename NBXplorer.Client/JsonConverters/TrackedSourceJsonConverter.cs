using NBitcoin;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using NBXplorer.Models;
using NBitcoin.JsonConverters;

namespace NBXplorer.JsonConverters
{
	public class TrackedSourceJsonConverter : JsonConverter
	{
		public TrackedSourceJsonConverter(Network network)
		{
			Network = network;
		}

		public Network Network { get; }

		public override bool CanConvert(Type objectType)
		{
			return
				typeof(TrackedSource).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			if (reader.TokenType == JsonToken.Null)
				return null;
			if (reader.TokenType != JsonToken.String)
				return null;

			if (TrackedSource.TryParse(reader.Value.ToString(), out var v, Network))
				return v;
			throw new JsonObjectException("Invalid TrackedSource", reader);
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			var trackedSource = value as TrackedSource;
			if (trackedSource != null)
			{
				writer.WriteValue(trackedSource.ToString());
			}
		}

	}
}
