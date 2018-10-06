using NBitcoin;
using System.Linq;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace NBXplorer.JsonConverters
{
	/// <summary>
	/// Cache serialization of costly base58 structures
	/// </summary>
	public class CachedSerializer : JsonConverter
	{
		class CachedConverter
		{
			public CachedConverter(JsonConverter converter)
			{
				Converter = converter;
			}

			public JsonConverter Converter { get; }
			ConcurrentDictionary<string, object> cachedStrings = new ConcurrentDictionary<string, object>();
			int total = 0;
			public object ReadJson(string str, JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
			{
				if (cachedStrings.TryGetValue(str, out var v))
					return v;
				v = Converter.ReadJson(reader, objectType, existingValue, serializer);
				if (cachedStrings.TryAdd(str, v))
				{
					Interlocked.Increment(ref total);
					if (total > 20)
					{
						cachedStrings.Clear();
						total = 0;
					}
				}
				return v;
			}

			public void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
			{
				Converter.WriteJson(writer, value, serializer);
			}

			internal bool CanConvert(Type objectType)
			{
				return Converter.CanConvert(objectType);
			}
		}
		public CachedSerializer()
		{

		}
		public CachedSerializer(Network network)
		{
			cachedConverter.Add(new CachedConverter(new BitcoinStringJsonConverter(network)));
			cachedConverter.Add(new CachedConverter(new DerivationStrategyJsonConverter(network == null ? null : new DerivationStrategy.DerivationStrategyFactory(network))));
			cachedConverter.Add(new CachedConverter(new TrackedSourceJsonConverter(network)));
		}
		public override bool CanConvert(Type objectType)
		{
			return GetConverter(objectType) != null;
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			if (reader.TokenType == JsonToken.Null)
				return null;

			var str = reader.Value.ToString();
			return GetConverter(objectType).ReadJson(str, reader, objectType, existingValue, serializer);
		}

		private CachedConverter GetConverter(Type objectType)
		{
			return cachedConverter.FirstOrDefault(s => s.CanConvert(objectType));
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			if (value == null)
				return;
			var converter = GetConverter(value.GetType());
			converter.WriteJson(writer, value, serializer);
		}

		List<CachedConverter> cachedConverter = new List<CachedConverter>();
	}
}