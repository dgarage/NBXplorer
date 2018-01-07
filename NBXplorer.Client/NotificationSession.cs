using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class NotificationSession : IDisposable
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
			uri = ToWebsocketUri(uri);
			WebSocket socket = null;
			try
			{
				socket = await ConnectAsyncCore(uri, cancellation);
			}
			catch(WebSocketException) // For some reason the ErrorCode is not properly set, so we can check for error 401
			{
				if(!_Client._Auth.RefreshCache())
					throw;
				socket = await ConnectAsyncCore(uri, cancellation);
			}
			JsonSerializerSettings settings = new JsonSerializerSettings();
			new Serializer(_Client.Network).ConfigureSerializer(settings);
			_MessageListener = new WebsocketMessageListener(socket, settings);
		}

		private async Task<ClientWebSocket> ConnectAsyncCore(string uri, CancellationToken cancellation)
		{
			var socket = new ClientWebSocket();
			_Client._Auth.SetWebSocketAuth(socket);
			try
			{
				await socket.ConnectAsync(new Uri(uri, UriKind.Absolute), cancellation).ConfigureAwait(false);
			}
			catch { socket.Dispose(); throw; }
			return socket;
		}

		private static string ToWebsocketUri(string uri)
		{
			if(uri.StartsWith("https://"))
				uri = uri.Replace("https://", "wss://");
			if(uri.StartsWith("http://"))
				uri = uri.Replace("http://", "ws://");
			return uri;
		}

		WebsocketMessageListener _MessageListener;
		UTF8Encoding UTF8 = new UTF8Encoding(false, true);

		public void ListenNewBlock(CancellationToken cancellation = default(CancellationToken))
		{
			ListenNewBlockAsync(cancellation).GetAwaiter().GetResult();
		}
		public Task ListenNewBlockAsync(CancellationToken cancellation = default(CancellationToken))
		{
			return _MessageListener.Send(new Models.NewBlockEventRequest(), cancellation);
		}

		public void ListenDerivationSchemes(DerivationStrategyBase[] derivationSchemes, CancellationToken cancellation = default(CancellationToken))
		{
			ListenDerivationSchemesAsync(derivationSchemes, cancellation).GetAwaiter().GetResult();
		}

		public Task ListenDerivationSchemesAsync(DerivationStrategyBase[] derivationSchemes, CancellationToken cancellation = default(CancellationToken))
		{
			return _MessageListener.Send(new Models.NewTransactionEventRequest() { DerivationSchemes = derivationSchemes }, cancellation);
		}

		public object NextEvent(CancellationToken cancellation = default(CancellationToken))
		{
			return NextEventAsync(cancellation).GetAwaiter().GetResult();
		}
		public Task<object> NextEventAsync(CancellationToken cancellation = default(CancellationToken))
		{
			return _MessageListener.NextMessageAsync(cancellation);
		}

		public Task DisposeAsync(CancellationToken cancellation = default(CancellationToken))
		{
			return _MessageListener.DisposeAsync(cancellation);
		}

		public void Dispose()
		{
			DisposeAsync().GetAwaiter().GetResult();
		}
	}
}
