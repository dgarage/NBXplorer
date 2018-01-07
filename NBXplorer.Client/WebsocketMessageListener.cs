using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NBXplorer
{
	public class WebsocketMessageListener
	{
		private readonly WebSocket _Socket;
		public WebSocket Socket
		{
			get
			{
				return _Socket;
			}
		}

		JsonSerializerSettings _SerializerSettings;
		public WebsocketMessageListener(WebSocket socket, JsonSerializerSettings serializerSettings)
		{
			_Socket = socket;
			_SerializerSettings = serializerSettings;
			var buffer = new byte[1024 * 4];
			_Buffer = new ArraySegment<byte>(buffer, 0, 1024);
		}

		ArraySegment<byte> _Buffer;

		UTF8Encoding UTF8 = new UTF8Encoding(false, true);
		public async Task<object> NextMessageAsync(CancellationToken cancellation)
		{
			var buffer = _Buffer;
			while(true)
			{
				var message = await Socket.ReceiveAsync(buffer, cancellation);
				if(message.MessageType == WebSocketMessageType.Close)
				{
					await CloseSocket(WebSocketCloseStatus.NormalClosure, "Close message received from the peer", cancellation);
					break;
				}
				if(message.MessageType != WebSocketMessageType.Text)
				{
					await CloseSocket(WebSocketCloseStatus.InvalidMessageType, "Only Text is supported", cancellation);
					break;
				}
				if(message.EndOfMessage)
				{
					buffer = _Buffer.Slice(0, buffer.Offset + message.Count);
					try
					{
						return ParseMessage(buffer);
					}
					catch(Exception ex)
					{
						await CloseSocket(WebSocketCloseStatus.InvalidPayloadData, $"Invalid payload: {ex.Message}", cancellation);
					}
				}
				else
				{
					if(buffer.Count - message.Count <= 0)
					{
						await CloseSocket(WebSocketCloseStatus.MessageTooBig, "Message is too big", cancellation);
						break;
					}
					buffer = _Buffer.Slice(message.Count, buffer.Count - message.Count);
				}
			}
			throw new InvalidOperationException("Should never happen");
		}

		private async Task CloseSocket(WebSocketCloseStatus status, string description, CancellationToken cancellation)
		{
			await Socket.CloseSocket(status, description, cancellation);
			throw new WebSocketException($"The socket has been closed ({status}: {description})");
		}
		

		private object ParseMessage(ArraySegment<byte> buffer)
		{
			var str = UTF8.GetString(buffer.Array, 0, buffer.Count);
			return ExtensionsClient.ParseNotificationMessage(str, _SerializerSettings);
		}

		public async Task Send<T>(T evt, CancellationToken cancellation = default(CancellationToken))
		{
			var typeName = ExtensionsClient.GetNotificationMessageTypeName(evt.GetType());
			if(typeName == null)
				throw new InvalidOperationException($"{evt.GetType().Name} does not have an associated typeName");
			JObject jobj = new JObject();
			var serialized = JsonConvert.SerializeObject(evt, _SerializerSettings);
			var data = JObject.Parse(serialized);
			jobj.Add(new JProperty("type", new JValue(typeName)));
			jobj.Add(new JProperty("data", data));
			var bytes = UTF8.GetBytes(jobj.ToString());

			using(var cts = new CancellationTokenSource(5000))
			{
				using(var cts2 = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
				{
					await Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts2.Token);
				}
			}
		}

		public async Task DisposeAsync(CancellationToken cancellation)
		{
			await Socket.CloseSocket(WebSocketCloseStatus.NormalClosure, "Disposing NotificationServer", cancellation);
		}
	}
}
