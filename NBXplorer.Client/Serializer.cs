using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
	public class Serializer
	{

		private readonly Network _Network;
		public Network Network
		{
			get
			{
				return _Network;
			}
		}

		public JsonSerializerSettings Settings { get; } = new JsonSerializerSettings();

		public Serializer(Network network)
		{
			_Network = network;
			ConfigureSerializer(Settings);
		}

		public void ConfigureSerializer(JsonSerializerSettings settings)
		{
			if(settings == null)
				throw new ArgumentNullException(nameof(settings));
			NBitcoin.JsonConverters.Serializer.RegisterFrontConverters(settings, Network);
			if (Network != null)
			{
				settings.Converters.Insert(0, new JsonConverters.CachedSerializer(Network));
			}
			settings.Converters.Insert(0, new JsonConverters.FeeRateJsonConverter());
		}

		public T ToObject<T>(string str)
		{
			return JsonConvert.DeserializeObject<T>(str, Settings);
		}

		public string ToString<T>(T obj)
		{
			return JsonConvert.SerializeObject(obj, Settings);
		}

		public T ToObject<T>(JObject jobj)
		{
			var serializer = JsonSerializer.Create(Settings);
			return jobj.ToObject<T>(serializer);
		}
	}
}
