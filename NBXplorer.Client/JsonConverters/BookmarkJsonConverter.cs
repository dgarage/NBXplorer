using NBitcoin;
using NBXplorer.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace NBXplorer.JsonConverters
{
	public class BookmarkJsonConverter : JsonConverter
	{
		public override bool CanConvert(Type objectType)
		{
			return
				typeof(Bookmark).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			if(reader.TokenType == JsonToken.Null)
				return null;

			return new Bookmark(new uint160(reader.Value.ToString()));
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			var b = value as Bookmark;
			if(b != null)
			{
				writer.WriteValue(b.ToString());
			}
		}
	}
}
