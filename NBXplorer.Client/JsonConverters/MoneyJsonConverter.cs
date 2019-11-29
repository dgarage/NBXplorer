using Newtonsoft.Json;
using System.Linq;
using System.Reflection;
using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using NBitcoin.JsonConverters;

namespace NBXplorer.JsonConverters
{
	public class MoneyJsonConverter : JsonConverter
	{
		public override bool CanConvert(Type objectType)
		{
			return typeof(IMoney).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			try
			{
				if (reader.TokenType == JsonToken.Null)
					return null;
				AssertJsonType(reader, new[] { JsonToken.Integer, JsonToken.String });
				if (reader.TokenType == JsonToken.Integer)
				{
					return new Money((long)reader.Value);
				}
				else
				{
					var splitted = ((string)reader.Value).Split(':');
					if (splitted.Length == 2 &&
						uint256.TryParse(splitted[0], out var assetId) &&
						long.TryParse(splitted[1], out var quantity))
					{
						return new AssetMoney(assetId, quantity);
					}
					throw new JsonObjectException("Invalid asset money, format should be \"assetid:quantity\"", reader);
				}
			}
			catch (InvalidCastException)
			{
				throw new JsonObjectException("Money amount should be in satoshi", reader);
			}
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			if (value is Money v)
				writer.WriteValue(v.Satoshi);
			else if (value is AssetMoney av)
				writer.WriteValue($"{av.AssetId}:{av.Quantity}");

		}
		static void AssertJsonType(JsonReader reader, JsonToken[] anyExpectedTypes)
		{
			if (!anyExpectedTypes.Contains(reader.TokenType))
				throw new JsonObjectException($"Unexpected json token type, expected are {string.Join(", ", anyExpectedTypes)} and actual is {reader.TokenType}", reader);
		}
	}
}
