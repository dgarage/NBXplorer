using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace NBXplorer.Models
{
	public abstract class NewEventBase
	{
		public string CryptoCode
		{
			get;
			set;
		}

		public JObject ToJObject(JsonSerializerSettings settings)
		{
			var typeName = ExtensionsClient.GetNotificationMessageTypeName(this.GetType());
			if (typeName == null)
				throw new InvalidOperationException($"{this.GetType().Name} does not have an associated typeName");
			JObject jobj = new JObject();
			var serialized = JsonConvert.SerializeObject(this, settings);
			var data = JObject.Parse(serialized);
			jobj.Add(new JProperty("type", new JValue(typeName)));
			jobj.Add(new JProperty("data", data));

			return jobj;
		}

		public string ToJson(JsonSerializerSettings settings)
		{
			return JsonConvert.SerializeObject(this, settings);
		}
	}
}
