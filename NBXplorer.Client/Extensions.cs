using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer
{
    public static class ExtensionsClient
    {
		static ExtensionsClient()
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

		public static IEnumerable<T[]> Batch<T>(this IEnumerable<T> values, int size)
		{
			var batch = new T[size];
			int index = 0;
			foreach(var v in values)
			{
				batch[index++] = v;
				if(index == batch.Length)
				{
					yield return batch;
					batch = new T[size];
					index = 0;
				}
			}
			if(index != 0)
			{
				Array.Resize(ref batch, index);
				yield return batch;
			}
		}

		public static ArraySegment<T> Slice<T>(this ArraySegment<T> array, int index)
		{
			
			if((uint)index > (uint)array.Count)
			{
				throw new ArgumentOutOfRangeException(nameof(index));
			}

			return new ArraySegment<T>(array.Array, array.Offset + index, array.Count - index);
		}

		public static ArraySegment<T> Slice<T>(this ArraySegment<T> array, int index, int count)
		{
			if((uint)index > (uint)array.Count || (uint)count > (uint)(array.Count - index))
			{
				throw new ArgumentOutOfRangeException(nameof(index));
			}

			return new ArraySegment<T>(array.Array, array.Offset + index, count);
		}

		public static object ParseNotificationMessage(string str, JsonSerializerSettings settings)
		{
			if(str == null)
				throw new ArgumentNullException(nameof(str));
			JObject jobj = JObject.Parse(str);
			var type = (jobj["type"] as JValue)?.Value<string>();
			if(type == null)
				throw new FormatException("'type' property not found");
			if(!_TypeByName.TryGetValue(type, out Type typeObject))
				throw new FormatException("unknown 'type'");
			var data = (jobj["data"] as JObject);
			if(data == null)
				throw new FormatException("'data' property not found");

			return JsonConvert.DeserializeObject(data.ToString(), typeObject, settings);
		}

		public static async Task CloseSocket(this WebSocket socket, WebSocketCloseStatus status, string statusDescription, CancellationToken cancellation = default)
		{
			try
			{
				if(socket.State == WebSocketState.Open)
				{
					using(CancellationTokenSource cts = new CancellationTokenSource())
					{
						cts.CancelAfter(5000);
						using(var cts2 = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellation))
						{
							try
							{
								await socket.CloseAsync(status, statusDescription, cts2.Token).ConfigureAwait(false);
							}
							catch(ObjectDisposedException) { }
						}
					}
				}
			}
			catch { }
			finally { socket.Dispose(); }
		}

		public static string GetNotificationMessageTypeName(Type type)
		{
			_NameByType.TryGetValue(type, out string name);
			return name;
		}
	}
}
