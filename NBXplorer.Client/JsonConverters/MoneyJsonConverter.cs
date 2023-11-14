using Newtonsoft.Json;
using System.Linq;
using System.Reflection;
using System;
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

		class AssetCoinJson
		{
			public uint256 AssetId { get; set; }
			public long? Value { get; set; }
			public AssetMoney ToAssetMoney(string path)
			{
				if (AssetId == null)
					throw new JsonObjectException("'assetId' is missing", path);
				if (Value is null)
					throw new JsonObjectException("'value' is missing", path);
				return new AssetMoney(AssetId, Value.Value);
			}
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			try
			{
				if (reader.TokenType == JsonToken.Null)
					return null;
				AssertJsonType(reader, new[] { JsonToken.Integer, JsonToken.StartObject, JsonToken.StartArray });
				if (reader.TokenType == JsonToken.Integer)
				{
					return new Money((long)reader.Value);
				}
				else if (reader.TokenType == JsonToken.StartObject)
				{
					return serializer.Deserialize<AssetCoinJson>(reader).ToAssetMoney(reader.Path);
				}
				else
				{
					return new MoneyBag(serializer.Deserialize<AssetCoinJson[]>(reader).Select(c => c.ToAssetMoney(reader.Path)).ToArray());
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
			{
				serializer.Serialize(writer, new AssetCoinJson() { Value = av.Quantity, AssetId = av.AssetId });
			}
			else if (value is MoneyBag mb)
			{
				serializer.Serialize(writer, mb.OfType<AssetMoney>().Select(av2 => new AssetCoinJson() { Value = av2.Quantity, AssetId = av2.AssetId }).ToArray());
			}
		}
		static void AssertJsonType(JsonReader reader, JsonToken[] anyExpectedTypes)
		{
			if (!anyExpectedTypes.Contains(reader.TokenType))
				throw new JsonObjectException($"Unexpected json token type, expected are {string.Join(", ", anyExpectedTypes)} and actual is {reader.TokenType}", reader);
		}
	}
}
