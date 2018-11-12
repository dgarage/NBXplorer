using Newtonsoft.Json;
using System.Linq;
using Newtonsoft.Json.Linq;
using System;
using System.Text;
using NBitcoin.DataEncoders;

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

		static Encoding UTF8 = new UTF8Encoding(false);
		public string GetEventId()
		{
			var str = String.Join("-", GetEventIdCore().Select(e => e == null ? string.Empty : e.ToString()).ToArray());
			var hash = NBitcoin.Crypto.Hashes.SHA256(UTF8.GetBytes(str));
			return Encoders.Hex.EncodeData(hash, 0, 20);
		}

		protected virtual object[] GetEventIdCore()
		{
			return new object[0];
		}

		public string ToJson(JsonSerializerSettings settings)
		{
			return JsonConvert.SerializeObject(this, settings);
		}
	}
}
