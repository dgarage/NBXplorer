using NBitcoin;
using System.Linq;
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
			var uri = _Client.GetFullUri($"v1/cryptos/{_Client.CryptoCode}/connect", null);
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
			new Serializer(_Client.Network.NBitcoinNetwork).ConfigureSerializer(settings);
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
			if(uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
				uri = uri.Replace("https://", "wss://");
			if(uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
				uri = uri.Replace("http://", "ws://");
			return uri;
		}

		WebsocketMessageListener _MessageListener;
		UTF8Encoding UTF8 = new UTF8Encoding(false, true);

		public void ListenNewBlock(CancellationToken cancellation = default)
		{
			ListenNewBlockAsync(cancellation).GetAwaiter().GetResult();
		}
		public Task ListenNewBlockAsync(CancellationToken cancellation = default)
		{
			return _MessageListener.Send(new Models.NewBlockEventRequest() { CryptoCode = _Client.CryptoCode }, cancellation);
		}

		/// <summary>
		/// Listen all derivation schemes
		/// </summary>
		/// <param name="allCryptoCodes">If true, all derivation schemes of all crypto code will get a notification (default: false)</param>
		/// <param name="cancellation">Cancellation token</param>
		public void ListenAllDerivationSchemes(bool allCryptoCodes = false, CancellationToken cancellation = default)
		{
			ListenAllDerivationSchemesAsync(allCryptoCodes, cancellation).GetAwaiter().GetResult();
		}

		/// <summary>
		/// Listen all derivation schemes
		/// </summary>
		/// <param name="allCryptoCodes">If true, all derivation schemes of all crypto code will get a notification (default: false)</param>
		/// <param name="cancellation">Cancellation token</param>
		public Task ListenAllDerivationSchemesAsync(bool allCryptoCodes = false, CancellationToken cancellation = default)
		{
			return _MessageListener.Send(new Models.NewTransactionEventRequest() { CryptoCode = allCryptoCodes ? "*" : _Client.CryptoCode }, cancellation);
		}

		/// <summary>
		/// Listen all tracked source
		/// </summary>
		/// <param name="allCryptoCodes">If true, all derivation schemes of all crypto code will get a notification (default: false)</param>
		/// <param name="cancellation">Cancellation token</param>
		public void ListenAllTrackedSource(bool allCryptoCodes = false, CancellationToken cancellation = default)
		{
			ListenAllTrackedSourceAsync(allCryptoCodes, cancellation).GetAwaiter().GetResult();
		}

		/// <summary>
		/// Listen all tracked source
		/// </summary>
		/// <param name="allCryptoCodes">If true, all derivation schemes of all crypto code will get a notification (default: false)</param>
		/// <param name="cancellation">Cancellation token</param>
		public Task ListenAllTrackedSourceAsync(bool allCryptoCodes = false, CancellationToken cancellation = default)
		{
			return _MessageListener.Send(new Models.NewTransactionEventRequest() { CryptoCode = allCryptoCodes ? "*" : _Client.CryptoCode, ListenAllTrackedSource = true }, cancellation);
		}

		public void ListenDerivationSchemes(DerivationStrategyBase[] derivationSchemes, CancellationToken cancellation = default)
		{
			ListenDerivationSchemesAsync(derivationSchemes, cancellation).GetAwaiter().GetResult();
		}

		public Task ListenDerivationSchemesAsync(DerivationStrategyBase[] derivationSchemes, CancellationToken cancellation = default)
		{
			return _MessageListener.Send(new Models.NewTransactionEventRequest() { DerivationSchemes = derivationSchemes.Select(d=>d.ToString()).ToArray(), CryptoCode = _Client.CryptoCode }, cancellation);
		}

		public void ListenTrackedSources(TrackedSource[] trackedSources, CancellationToken cancellation = default)
		{
			ListenTrackedSourcesAsync(trackedSources, cancellation).GetAwaiter().GetResult();
		}

		public Task ListenTrackedSourcesAsync(TrackedSource[] trackedSources, CancellationToken cancellation = default)
		{
			return _MessageListener.Send(new Models.NewTransactionEventRequest() { TrackedSources = trackedSources.Select(d => d.ToString()).ToArray(), CryptoCode = _Client.CryptoCode }, cancellation);
		}

		public object NextEvent(CancellationToken cancellation = default)
		{
			return NextEventAsync(cancellation).GetAwaiter().GetResult();
		}
		public Task<object> NextEventAsync(CancellationToken cancellation = default)
		{
			return _MessageListener.NextMessageAsync(cancellation);
		}

		public Task DisposeAsync(CancellationToken cancellation = default)
		{
			return _MessageListener.DisposeAsync(cancellation);
		}

		public void Dispose()
		{
			DisposeAsync().GetAwaiter().GetResult();
		}
	}
}
