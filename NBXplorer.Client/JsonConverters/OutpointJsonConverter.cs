using NBitcoin;
using System.Reflection;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin.JsonConverters;

namespace NBXplorer.JsonConverters
{
	public class OutpointJsonConverter : JsonConverter
	{
		public OutpointJsonConverter()
		{
		}
		public override bool CanConvert(Type objectType)
		{
			return
				typeof(OutPoint).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			if(reader.TokenType == JsonToken.Null)
				return null;

			if(reader.TokenType != JsonToken.StartObject)
				return null;

			reader.Read();
			OutPoint outPoint = new OutPoint();
			bool exit = false;
			while(!exit)
			{
				switch(reader.TokenType)
				{
					case JsonToken.PropertyName:
						{
							switch(reader.Value.ToString())
							{
								case "hash":
									reader.Read();
									outPoint.Hash = new uint256((string)reader.Value);
									break;
								case "index":
									reader.Read();
									outPoint.N = (uint)(Int64)reader.Value;
									break;
							}
							break;
						}
					case JsonToken.EndObject:
						exit = true;
						break;
				}
				if(!exit)
					reader.Read();
			}
			return outPoint;
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			var outpoint = value as OutPoint;
			if(outpoint != null)
			{
				writer.WriteStartObject();
				writer.WritePropertyName("hash");
				writer.WriteValue(outpoint.Hash.ToString());
				writer.WritePropertyName("index");
				writer.WriteValue(outpoint.N);
				writer.WriteEndObject();
			}
		}

		public DerivationStrategyFactory Factory
		{
			get;
			set;
		}
	}
}
