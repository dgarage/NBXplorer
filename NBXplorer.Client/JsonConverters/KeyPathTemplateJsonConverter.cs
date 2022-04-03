using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.JsonConverters
{
	public class KeyPathTemplateJsonConverter : JsonConverter<KeyPathTemplate>
	{
		public override KeyPathTemplate ReadJson(JsonReader reader, Type objectType, KeyPathTemplate existingValue, bool hasExistingValue, JsonSerializer serializer)
		{
			if (reader.TokenType == JsonToken.Null)
				return null;
			if (reader.TokenType != JsonToken.String)
				throw new NBitcoin.JsonConverters.JsonObjectException($"Unexpected json token type, expected String, actual {reader.TokenType}", reader);
			if (!KeyPathTemplate.TryParse((string)reader.Value, out var template))
				throw new NBitcoin.JsonConverters.JsonObjectException($"Invalid KeyPathTemplate", reader);
			return template;
		}

		public override void WriteJson(JsonWriter writer, KeyPathTemplate value, JsonSerializer serializer)
		{
			if (value is KeyPathTemplate kt)
				writer.WriteValue(value.ToString());
		}
	}
}
