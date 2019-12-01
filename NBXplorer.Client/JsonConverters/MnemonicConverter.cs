using NBitcoin;
using Newtonsoft.Json;
using System;
using System.Reflection;

namespace NBXplorer.JsonConverters
{
	public class MnemonicConverter : JsonConverter
	{
		public override bool CanConvert(Type objectType)
		{
			return typeof(NBitcoin.Mnemonic).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			if (reader.TokenType == JsonToken.Null)
				return null;
			if (reader.TokenType != JsonToken.String)
				throw new NBitcoin.JsonConverters.JsonObjectException($"Unexpected json token type, expected String, actual {reader.TokenType}", reader);
			return new Mnemonic((string) reader.Value);
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			if (value is Mnemonic t)
				writer.WriteValue(t.ToString());
		}
	}
}
