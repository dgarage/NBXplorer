using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.JsonConverters;
using NBXplorer.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace NBXplorer.JsonConverters
{
	public class PSBTDestinationJsonConverter : JsonConverter
	{
		public PSBTDestinationJsonConverter(Network network)
		{
			Network = network;
		}

		public Network Network { get; }

		public override bool CanConvert(Type objectType)
		{
			return typeof(PSBTDestination).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			if (reader.TokenType == JsonToken.Null)
				return null;
			if (reader.TokenType != JsonToken.String)
			{
				throw new JsonObjectException($"Unexpected json token type, expected is {JsonToken.String} and actual is {reader.TokenType}", reader);
			}
			var str = reader.Value.ToString();
			try
			{
				return PSBTDestination.Parse(str, Network);
			}
			catch (FormatException ex)
			{
				throw new JsonObjectException(ex.Message, reader);
			}
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			if (value is not null)
			{
				writer.WriteValue(value.ToString());
			}
		}
	}
}
