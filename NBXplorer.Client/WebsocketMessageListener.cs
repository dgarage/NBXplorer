﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBXplorer.Models;
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
			var buffer = new byte[ORIGINAL_BUFFER_SIZE];
			_Buffer = new ArraySegment<byte>(buffer, 0, buffer.Length);
		}

		const int ORIGINAL_BUFFER_SIZE = 1024 * 5;
		const int MAX_BUFFER_SIZE = 1024 * 1024 * 5;

		ArraySegment<byte> _Buffer;

		UTF8Encoding UTF8 = new UTF8Encoding(false, true);
		public async Task<object> NextMessageAsync(CancellationToken cancellation)
		{
			var buffer = _Buffer;
			var array = _Buffer.Array;
			var originalSize = _Buffer.Array.Length;
			var newSize = _Buffer.Array.Length;
			while(true)
			{
				var message = await Socket.ReceiveAsync(buffer, cancellation);
				if(message.MessageType == WebSocketMessageType.Close)
				{
					await CloseSocketAndThrow(WebSocketCloseStatus.NormalClosure, "Close message received from the peer", cancellation);
					break;
				}
				if(message.MessageType != WebSocketMessageType.Text)
				{
					await CloseSocketAndThrow(WebSocketCloseStatus.InvalidMessageType, "Only Text is supported", cancellation);
					break;
				}
				if(message.EndOfMessage)
				{
					buffer = new ArraySegment<byte>(array, 0, buffer.Offset + message.Count);
					try
					{
						var o = ParseMessage(buffer);
						if(newSize != originalSize)
						{
							Array.Resize(ref array, originalSize);
						}
						return o;
					}
					catch(Exception ex)
					{
						await CloseSocketAndThrow(WebSocketCloseStatus.InvalidPayloadData, $"Invalid payload: {ex.Message}", cancellation);
					}
				}
				else
				{
					if(buffer.Count - message.Count <= 0)
					{
						newSize *= 2;
						if(newSize > MAX_BUFFER_SIZE)
							await CloseSocketAndThrow(WebSocketCloseStatus.MessageTooBig, "Message is too big", cancellation);
						Array.Resize(ref array, newSize);
						buffer = new ArraySegment<byte>(array, buffer.Offset, newSize - buffer.Offset);
					}
					
					buffer = buffer.Slice(message.Count, buffer.Count - message.Count);
				}
			}
			throw new InvalidOperationException("Should never happen");
		}

		private async Task CloseSocketAndThrow(WebSocketCloseStatus status, string description, CancellationToken cancellation)
		{
			var array = _Buffer.Array;
			if(array.Length != ORIGINAL_BUFFER_SIZE)
				Array.Resize(ref array, ORIGINAL_BUFFER_SIZE);
			await Socket.CloseSocket(status, description, cancellation);
			throw new WebSocketException($"The socket has been closed ({status}: {description})");
		}
		

		private object ParseMessage(ArraySegment<byte> buffer)
		{
			var str = UTF8.GetString(buffer.Array, 0, buffer.Count);
			return ExtensionsClient.ParseNotificationMessage(str, _SerializerSettings);
		}

		public async Task Send(NewEventBase evt, CancellationToken cancellation = default)
		{
			//var typeName = ExtensionsClient.GetNotificationMessageTypeName(evt.GetType());
			//if(typeName == null)
			//	throw new InvalidOperationException($"{evt.GetType().Name} does not have an associated typeName");
			//JObject jobj = new JObject();
			//var serialized = JsonConvert.SerializeObject(evt, _SerializerSettings);
			//var data = JObject.Parse(serialized);
			//jobj.Add(new JProperty("type", new JValue(typeName)));
			//jobj.Add(new JProperty("data", data
			
			var bytes = UTF8.GetBytes(evt.ToJObject(_SerializerSettings).ToString());

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
			try
			{
				await Socket.CloseSocket(WebSocketCloseStatus.NormalClosure, "Disposing NotificationServer", cancellation);
			}
			catch { }
			finally { try { Socket.Dispose(); } catch { } }
		}
	}
}
