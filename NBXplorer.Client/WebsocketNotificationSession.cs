using System.Linq;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin.Protocol;

namespace NBXplorer
{
	public class WebsocketNotificationSessionLegacy : WebsocketNotificationSession
	{
		protected override FormattableString GetConnectPath() => $"v1/cryptos/{_Client.CryptoCode}/connect";
		internal WebsocketNotificationSessionLegacy(ExplorerClient client) : base(client)
		{
		}
		public void ListenNewBlock(CancellationToken cancellation = default)
		{
			ListenNewBlockAsync(cancellation).GetAwaiter().GetResult();
		}
		public Task ListenNewBlockAsync(CancellationToken cancellation = default)
		{
			return _MessageListener.Send(new Models.NewBlockEventRequest() { CryptoCode = _Client.CryptoCode }, null, cancellation);
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
			return _MessageListener.Send(new Models.NewTransactionEventRequest() { CryptoCode = allCryptoCodes ? "*" : _Client.CryptoCode }, null, cancellation);
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
			return _MessageListener.Send(new Models.NewTransactionEventRequest() { CryptoCode = allCryptoCodes ? "*" : _Client.CryptoCode, ListenAllTrackedSource = true }, null, cancellation);
		}

		public void ListenDerivationSchemes(DerivationStrategyBase[] derivationSchemes, CancellationToken cancellation = default)
		{
			ListenDerivationSchemesAsync(derivationSchemes, cancellation).GetAwaiter().GetResult();
		}

		public Task ListenDerivationSchemesAsync(DerivationStrategyBase[] derivationSchemes, CancellationToken cancellation = default)
		{
			return _MessageListener.Send(new Models.NewTransactionEventRequest() { DerivationSchemes = derivationSchemes.Select(d => d.ToString()).ToArray(), CryptoCode = _Client.CryptoCode }, null, cancellation);
		}

		public void ListenTrackedSources(TrackedSource[] trackedSources, CancellationToken cancellation = default)
		{
			ListenTrackedSourcesAsync(trackedSources, cancellation).GetAwaiter().GetResult();
		}

		public Task ListenTrackedSourcesAsync(TrackedSource[] trackedSources, CancellationToken cancellation = default)
		{
			return _MessageListener.Send(new Models.NewTransactionEventRequest() { TrackedSources = trackedSources.Select(d => d.ToString()).ToArray(), CryptoCode = _Client.CryptoCode }, null, cancellation);
		}

	}
	public class WebsocketNotificationSession : NotificationSessionBase, IDisposable
	{

		protected readonly ExplorerClient _Client;
		public ExplorerClient Client
		{
			get
			{
				return _Client;
			}
		}
		internal WebsocketNotificationSession(ExplorerClient client)
		{
			if(client == null)
				throw new ArgumentNullException(nameof(client));
			_Client = client;
		}

		internal async Task ConnectAsync(CancellationToken cancellation)
		{
			var uri = _Client.GetFullUri(GetConnectPath());
			uri = ToWebsocketUri(uri);
			WebSocket socket = null;
			try
			{
				socket = await ConnectAsyncCore(uri, cancellation);
			}
			catch (WebSocketException) // For some reason the ErrorCode is not properly set, so we can check for error 401
			{
				if (!_Client.Auth.RefreshCache())
					throw;
				socket = await ConnectAsyncCore(uri, cancellation);
			}
			JsonSerializerSettings settings = new JsonSerializerSettings();
			new Serializer(_Client.Network).ConfigureSerializer(settings);
			_MessageListener = new WebsocketMessageListener(socket, settings);
		}

		protected virtual FormattableString GetConnectPath() => $"v1/cryptos/connect?cryptoCode={_Client.Network.CryptoCode}";

		private async Task<ClientWebSocket> ConnectAsyncCore(string uri, CancellationToken cancellation)
		{
			var socket = new ClientWebSocket();
			_Client.Auth.SetWebSocketAuth(socket);
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

		protected WebsocketMessageListener _MessageListener;

		public override Task<NewEventBase> NextEventAsync(CancellationToken cancellation = default)
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
