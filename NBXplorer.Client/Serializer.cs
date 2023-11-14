using NBitcoin;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace NBXplorer
{
	public class Serializer
	{

		private readonly NBXplorerNetwork _Network;
		public Network Network => _Network?.NBitcoinNetwork;

		public JsonSerializerSettings Settings { get; } = new JsonSerializerSettings();
		public Serializer(NBXplorerNetwork network)
		{
			_Network = network;
			ConfigureSerializer(Settings);
		}

		public void ConfigureSerializer(JsonSerializerSettings settings)
		{
			if (settings == null)
				throw new ArgumentNullException(nameof(settings));
			Settings.DateParseHandling = DateParseHandling.None;
			NBitcoin.JsonConverters.Serializer.RegisterFrontConverters(settings, Network);
			if (_Network != null)
			{
				settings.Converters.Insert(0, new JsonConverters.CachedSerializer(_Network));
				settings.Converters.Add(new JsonConverters.KeyPathTemplateJsonConverter());
			}
			ReplaceConverter<NBitcoin.JsonConverters.MoneyJsonConverter>(settings, new NBXplorer.JsonConverters.MoneyJsonConverter());
		}

		private static void ReplaceConverter<T>(JsonSerializerSettings settings, JsonConverter jsonConverter) where T : JsonConverter
		{
			var moneyConverter = settings.Converters.OfType<T>().Single();
			var index = settings.Converters.IndexOf(moneyConverter);
			settings.Converters.RemoveAt(index);
			settings.Converters.Insert(index, new NBXplorer.JsonConverters.MoneyJsonConverter());
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
