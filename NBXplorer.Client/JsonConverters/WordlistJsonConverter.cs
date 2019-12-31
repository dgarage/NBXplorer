using NBitcoin;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.JsonConverters
{
	public class WordlistJsonConverter : JsonConverter
	{
		static WordlistJsonConverter()
		{
			_Wordlists = new Dictionary<string, Wordlist>(StringComparer.OrdinalIgnoreCase)
			{
				{ "English", Wordlist.English },
				{ "French", Wordlist.French },
				{ "Japanese", Wordlist.Japanese },
				{ "Spanish", Wordlist.Spanish },
				{ "ChineseSimplified", Wordlist.ChineseSimplified }
			};
			_WordlistsReverse = _Wordlists.ToDictionary(kv => kv.Value, kv => kv.Key);
		}
		public override bool CanConvert(Type objectType)
		{
			return typeof(Wordlist).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			if (reader.TokenType == JsonToken.Null)
				return null;
			if (reader.TokenType != JsonToken.String)
				throw new NBitcoin.JsonConverters.JsonObjectException($"Unexpected json token type, expected String, actual {reader.TokenType}", reader);
			if (!_Wordlists.TryGetValue((string)reader.Value, out var result))
				throw new NBitcoin.JsonConverters.JsonObjectException($"Invalid wordlist, possible values {string.Join(", ", _Wordlists.Keys.ToArray())} (defaut: English)", reader);
			return result;
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			if (value is Wordlist wl)
				writer.WriteValue(_WordlistsReverse[wl]);
		}

		readonly static Dictionary<string, Wordlist> _Wordlists;
		readonly static Dictionary<Wordlist, string> _WordlistsReverse;
	}
}
