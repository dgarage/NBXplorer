using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace NBXplorer.Models
{
	public abstract class NewEventBase
	{
		static NewEventBase()
		{
			_TypeByName = new Dictionary<string, Type>();
			_NameByType = new Dictionary<Type, string>();
			Add("newblock", typeof(Models.NewBlockEvent));
			Add("subscribeblock", typeof(Models.NewBlockEventRequest));
			Add("subscribetransaction", typeof(Models.NewTransactionEventRequest));
			Add("newtransaction", typeof(Models.NewTransactionEvent));
		}

		static Dictionary<string, Type> _TypeByName;
		static Dictionary<Type, string> _NameByType;
		private static void Add(string typeName, Type type)
		{
			_TypeByName.Add(typeName, type);
			_NameByType.Add(type, typeName);
		}
		public static string GetEventTypeName(Type type)
		{
			_NameByType.TryGetValue(type, out string name);
			return name;
		}
		public string CryptoCode
		{
			get;
			set;
		}

		public JObject ToJObject(JsonSerializerSettings settings)
		{
			var typeName = GetEventTypeName(this.GetType());
			if (typeName == null)
				throw new InvalidOperationException($"{this.GetType().Name} does not have an associated typeName");
			JObject jobj = new JObject();
			var serialized = JsonConvert.SerializeObject(this, settings);
			var data = JObject.Parse(serialized);
			jobj.Add(new JProperty("type", new JValue(typeName)));
			jobj.Add(new JProperty("data", data));

			return jobj;
		}

		public static NewEventBase ParseEvent(string str, JsonSerializerSettings settings)
		{
			if (str == null)
				throw new ArgumentNullException(nameof(str));
			JObject jobj = JObject.Parse(str);
			var type = (jobj["type"] as JValue)?.Value<string>();
			if (type == null)
				throw new FormatException("'type' property not found");
			if (!_TypeByName.TryGetValue(type, out Type typeObject))
				throw new FormatException("unknown 'type'");
			var data = (jobj["data"] as JObject);
			if (data == null)
				throw new FormatException("'data' property not found");

			return (NewEventBase)JsonConvert.DeserializeObject(data.ToString(), typeObject, settings);
		}

		public string ToJson(JsonSerializerSettings settings)
		{
			return JsonConvert.SerializeObject(this, settings);
		}
	}
}
