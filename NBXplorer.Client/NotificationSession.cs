using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer.Client
{
	public class NotificationSession
	{

		private readonly ExplorerClient _Client;
		public ExplorerClient Client
		{
			get
			{
				return _Client;
			}
		}
		internal NotificationSession(ExplorerClient client)
		{
			if(client == null)
				throw new ArgumentNullException(nameof(client));
			_Client = client;
		}

		internal async Task ConnectAsync(CancellationToken cancellation)
		{
			Dictionary<string, string> parameters = new Dictionary<string, string>();
			var uri = _Client.GetFullUri("v1/connect", parameters);
			if(uri.StartsWith("https://"))
				uri = uri.Replace("https://", "wss://");
			if(uri.StartsWith("http://"))
				uri = uri.Replace("http://", "ws://");
			_Socket = new ClientWebSocket();
			await _Socket.ConnectAsync(new Uri(uri), cancellation).ConfigureAwait(false);
			//socket.
		}

		UTF8Encoding UTF8 = new UTF8Encoding(false, true);
		public async Task ListenNewBlock(NewBlockEventRequest newBlockRequest, CancellationToken cancellation = default(CancellationToken))
		{
			var serializer = new Serializer(_Client.Network);
			var request = serializer.ToString(new NBXplorerEventRequest(newBlockRequest));
			var bytes = UTF8.GetBytes(request);
			await _Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation);
		}

		public async Task<NBXplorerEvent> NextEventAsync(CancellationToken cancellation = default(CancellationToken))
		{
			byte[] buffer = new byte[1024];
			while(true)
			{
				var result = await _Socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation);
				if(result.MessageType == WebSocketMessageType.Text)
				{
					var serializer = new Serializer(_Client.Network);
					return serializer.ToObject<NBXplorerEvent>(UTF8.GetString(buffer, 0, result.Count));
				}
			}
		}

		ClientWebSocket _Socket;
		private async Task CloseSocket(CancellationToken cancellation)
		{
			try
			{
				if(_Socket.State == WebSocketState.Open)
				{
					CancellationTokenSource cts = new CancellationTokenSource();
					cts.CancelAfter(5000);
					await _Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cts.Token);
				}
			}
			catch { }
			finally { _Socket.Dispose(); }
		}
	}
}
