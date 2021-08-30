using NBitcoin;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.JsonConverters
{
	public class ScriptPubKeyTypeConverter : JsonConverter
	{
		static ScriptPubKeyTypeConverter()
		{
			_ScriptPubKeyType = new Dictionary<string, ScriptPubKeyType>()
			{
				{ "Legacy", ScriptPubKeyType.Legacy },
				{ "Segwit", ScriptPubKeyType.Segwit },
				{ "SegwitP2SH", ScriptPubKeyType.SegwitP2SH },
#pragma warning disable CS0618 // Type or member is obsolete
				{ "Taproot", ScriptPubKeyType.TaprootBIP86 }
#pragma warning restore CS0618 // Type or member is obsolete
			};
			_ScriptPubKeyTypeReverse = _ScriptPubKeyType.ToDictionary(kv => kv.Value, kv => kv.Key);
		}
		public override bool CanConvert(Type objectType)
		{
			return typeof(NBitcoin.WordCount).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			if (reader.TokenType == JsonToken.Null)
				return null;
			if (reader.TokenType != JsonToken.String)
				throw new NBitcoin.JsonConverters.JsonObjectException($"Unexpected json token type, expected String, actual {reader.TokenType}", reader);
			if (!_ScriptPubKeyType.TryGetValue((string)reader.Value, out var result))
				throw new NBitcoin.JsonConverters.JsonObjectException($"Invalid ScriptPubKeyType, possible values {string.Join(", ", _ScriptPubKeyType.Keys.ToArray())} (defaut: 12)", reader);
			return result;
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			if (value is ScriptPubKeyType t)
				writer.WriteValue(_ScriptPubKeyTypeReverse[t]);
		}

		readonly static Dictionary<string, ScriptPubKeyType> _ScriptPubKeyType;
		readonly static Dictionary<ScriptPubKeyType, string> _ScriptPubKeyTypeReverse;
	}
}
