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
	public class DerivationStrategyJsonConverter : JsonConverter
	{
		public DerivationStrategyJsonConverter(DerivationStrategy.DerivationStrategyFactory factory)
		{
			Factory = factory;
		}
		public override bool CanConvert(Type objectType)
		{
			return
				typeof(DerivationStrategyBase).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			if(reader.TokenType == JsonToken.Null)
				return null;

			try
			{
				var result = Factory?.Parse(reader.Value.ToString());
				if(result == null)
				{
					throw new JsonObjectException("Invalid derivation strategy", reader);
				}
				if(!objectType.GetTypeInfo().IsAssignableFrom(result.GetType().GetTypeInfo()))
				{
					throw new JsonObjectException("Invalid derivation strategy expected " + objectType.Name + ", actual " + result.GetType().Name, reader);
				}
				return result;
			}
			catch(FormatException)
			{
				throw new JsonObjectException("Invalid derivation strategy", reader);
			}
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			var base58 = value as DerivationStrategyBase;
			if(base58 != null)
			{
				writer.WriteValue(base58.ToString());
			}
		}

		public DerivationStrategyFactory Factory
		{
			get;
			set;
		}
	}
}
