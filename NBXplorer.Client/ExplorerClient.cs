using NBitcoin;
using System.Linq;
using NBitcoin.DataEncoders;
using NBitcoin.JsonConverters;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBXplorer.Configuration;
using System.Net.WebSockets;

namespace NBXplorer
{
	public class ExplorerClient
	{
		internal interface IAuth
		{
			bool RefreshCache();
			void SetAuthorization(HttpRequestMessage message);
			void SetWebSocketAuth(ClientWebSocket socket);
		}

		class CookieAuthentication : IAuth
		{
			string _CookieFilePath;
			AuthenticationHeaderValue _CachedAuth;

			public CookieAuthentication(string path)
			{
				_CookieFilePath = path;
			}

			public bool RefreshCache()
			{
				try
				{
					var cookieData = File.ReadAllText(_CookieFilePath);
					_CachedAuth = new AuthenticationHeaderValue("Basic", Encoders.Base64.EncodeData(Encoders.ASCII.DecodeData(cookieData)));
					return true;
				}
				catch { return false; }
			}

			public void SetAuthorization(HttpRequestMessage message)
			{
				message.Headers.Authorization = _CachedAuth;
			}

			public void SetWebSocketAuth(ClientWebSocket socket)
			{
				socket.Options.SetRequestHeader("Authorization", $"{_CachedAuth.Scheme} {_CachedAuth.Parameter}");
			}
		}
		class NullAuthentication : IAuth
		{
			public bool RefreshCache()
			{
				return false;
			}

			public void SetAuthorization(HttpRequestMessage message)
			{
			}

			public void SetWebSocketAuth(ClientWebSocket socket)
			{
			}
		}

		public ExplorerClient(Network network, Uri serverAddress)
			: this(NetworkInformation.GetNetworkByName(network?.Name) ?? throw new ArgumentException("unsupported network", "network"), serverAddress)
		{
		}
		public ExplorerClient(NetworkInformation network, Uri serverAddress)
		{
			if(serverAddress == null)
				throw new ArgumentNullException(nameof(serverAddress));
			if(network == null)
				throw new ArgumentNullException(nameof(network));
			_Address = serverAddress;
			_NetworkInformation = network;
			_Serializer = new Serializer(network.Network);
			_Factory = new DerivationStrategy.DerivationStrategyFactory(Network);
			var auth = new CookieAuthentication(Path.Combine(network.DefaultDataDirectory, ".cookie"));
			if(auth.RefreshCache())
				_Auth = auth;
		}

		internal IAuth _Auth = new NullAuthentication();

		public bool SetCookieAuth(string path)
		{
			if(path == null)
				throw new ArgumentNullException(nameof(path));
			CookieAuthentication auth = new CookieAuthentication(path);
			if(!auth.RefreshCache())
				return false;
			_Auth = auth;
			return true;
		}

		public void SetNoAuth()
		{
			_Auth = new NullAuthentication();
		}

		Serializer _Serializer;
		DerivationStrategy.DerivationStrategyFactory _Factory;
		public UTXOChanges Sync(DerivationStrategyBase extKey, UTXOChanges previousChange, bool noWait = false, CancellationToken cancellation = default(CancellationToken))
		{
			return SyncAsync(extKey, previousChange, noWait, cancellation).GetAwaiter().GetResult();
		}

		public async Task<TransactionResult> GetTransactionAsync(uint256 txId, CancellationToken cancellation = default(CancellationToken))
		{
			return await SendAsync<TransactionResult>(HttpMethod.Get, null, "v1/tx/" + txId, null, cancellation).ConfigureAwait(false);
		}

		public TransactionResult GetTransaction(uint256 txId, CancellationToken cancellation = default(CancellationToken))
		{
			return GetTransactionAsync(txId, cancellation).GetAwaiter().GetResult();
		}

		public Task<UTXOChanges> SyncAsync(DerivationStrategyBase extKey, UTXOChanges previousChange, bool noWait = false, CancellationToken cancellation = default(CancellationToken))
		{
			return SyncAsync(extKey, previousChange?.Confirmed?.Hash, previousChange?.Unconfirmed?.Hash, noWait, cancellation);
		}

		public UTXOChanges Sync(DerivationStrategyBase extKey, uint256 confHash, uint256 unconfirmedHash, bool noWait = false, CancellationToken cancellation = default(CancellationToken))
		{
			return SyncAsync(extKey, confHash, unconfirmedHash, noWait, cancellation).GetAwaiter().GetResult();
		}

		public NotificationSession CreateNotificationSession(CancellationToken cancellation = default(CancellationToken))
		{
			return CreateNotificationSessionAsync(cancellation).GetAwaiter().GetResult();
		}

		public async Task<NotificationSession> CreateNotificationSessionAsync(CancellationToken cancellation = default(CancellationToken))
		{
			var session = new NotificationSession(this);
			await session.ConnectAsync(cancellation).ConfigureAwait(false);
			return session;
		}

		public async Task<UTXOChanges> SyncAsync(DerivationStrategyBase extKey, uint256 confHash, uint256 unconfHash, bool noWait = false, CancellationToken cancellation = default(CancellationToken))
		{
			Dictionary<string, string> parameters = new Dictionary<string, string>();
			if(confHash != null)
				parameters.Add("confHash", confHash.ToString());
			if(unconfHash != null)
				parameters.Add("unconfHash", unconfHash.ToString());
			parameters.Add("noWait", noWait.ToString());

			var query = String.Join("&", parameters.Select(p => p.Key + "=" + p.Value).ToArray());
			return await SendAsync<UTXOChanges>(HttpMethod.Get, null, "v1/sync/{0}?" + query, new object[] { extKey.ToString() }, cancellation).ConfigureAwait(false);
		}

		public void WaitServerStarted(CancellationToken cancellation = default(CancellationToken))
		{
			WaitServerStartedAsync(cancellation).GetAwaiter().GetResult();
		}
		public async Task WaitServerStartedAsync(CancellationToken cancellation = default(CancellationToken))
		{
			while(true)
			{
				try
				{
					var status = await GetStatusAsync(cancellation).ConfigureAwait(false);
					if(status.IsFullySynched)
						break;
				}
				catch(OperationCanceledException) { throw; }
				catch { }
				cancellation.ThrowIfCancellationRequested();
			}
		}


		public void Track(DerivationStrategyBase strategy, CancellationToken cancellation = default(CancellationToken))
		{
			TrackAsync(strategy, cancellation).GetAwaiter().GetResult();
		}
		public Task TrackAsync(DerivationStrategyBase strategy, CancellationToken cancellation = default(CancellationToken))
		{
			return SendAsync<string>(HttpMethod.Post, null, "v1/track/{0}", new[] { strategy.ToString() }, cancellation);
		}

		public void CancelReservation(DerivationStrategyBase strategy, KeyPath[] keyPaths, CancellationToken cancellation = default(CancellationToken))
		{
			CancelReservationAsync(strategy, keyPaths, cancellation).GetAwaiter().GetResult();
		}

		public Task CancelReservationAsync(DerivationStrategyBase strategy, KeyPath[] keyPaths, CancellationToken cancellation = default(CancellationToken))
		{
			return SendAsync<string>(HttpMethod.Post, keyPaths, "v1/addresses/{0}/cancelreservation", new[] { strategy.ToString() }, cancellation);
		}

		public StatusResult GetStatus(CancellationToken cancellation = default(CancellationToken))
		{
			return GetStatusAsync(cancellation).GetAwaiter().GetResult();
		}

		public Task<StatusResult> GetStatusAsync(CancellationToken cancellation = default(CancellationToken))
		{
			return SendAsync<StatusResult>(HttpMethod.Get, null, "v1/status", null, cancellation);
		}

		public KeyPathInformation GetUnused(DerivationStrategyBase strategy, DerivationFeature feature, int skip = 0, bool reserve = false, CancellationToken cancellation = default(CancellationToken))
		{
			return GetUnusedAsync(strategy, feature, skip, reserve, cancellation).GetAwaiter().GetResult();
		}

		public async Task<KeyPathInformation> GetUnusedAsync(DerivationStrategyBase strategy, DerivationFeature feature, int skip = 0, bool reserve = false, CancellationToken cancellation = default(CancellationToken))
		{
			try
			{
				return await GetAsync<KeyPathInformation>("v1/addresses/{0}/unused?feature={1}&skip={2}&reserve={3}", new object[] { strategy.ToString(), feature, skip, reserve }, cancellation).ConfigureAwait(false);
			}
			catch(NBXplorerException ex) when(ex.Error?.HttpCode == 404)
			{
				return null;
			}
		}


		public GetFeeRateResult GetFeeRate(int blockCount, CancellationToken cancellation = default(CancellationToken))
		{
			return GetFeeRateAsync(blockCount, cancellation).GetAwaiter().GetResult();
		}

		public Task<GetFeeRateResult> GetFeeRateAsync(int blockCount, CancellationToken cancellation = default(CancellationToken))
		{
			return GetAsync<GetFeeRateResult>("v1/fees/{0}", new object[] { blockCount }, cancellation);
		}

		public void SubscribeToWallet(Uri uri, DerivationStrategyBase strategy, CancellationToken cancellation = default(CancellationToken))
		{
			SubscribeToWalletAsync(uri, strategy, cancellation).GetAwaiter().GetResult();
		}

		public Task SubscribeToWalletAsync(Uri uri, DerivationStrategyBase strategy, CancellationToken cancellation = default(CancellationToken))
		{
			return SendAsync<string>(HttpMethod.Post, new SubscribeToBlockRequest() { Callback = uri }, "v1/addresses/{0}/subscriptions", new[] { strategy }, cancellation);
		}

		public void SubscribeToBlocks(Uri uri, CancellationToken cancellation = default(CancellationToken))
		{
			SubscribeToBlocksAsync(uri, cancellation).GetAwaiter().GetResult();
		}

		public Task SubscribeToBlocksAsync(Uri uri, CancellationToken cancellation = default(CancellationToken))
		{
			return SendAsync<string>(HttpMethod.Post, new SubscribeToBlockRequest() { Callback = uri }, "v1/subscriptions/blocks", null, cancellation);
		}

		public BroadcastResult Broadcast(Transaction tx, CancellationToken cancellation = default(CancellationToken))
		{
			return BroadcastAsync(tx, cancellation).GetAwaiter().GetResult();
		}

		public Task<BroadcastResult> BroadcastAsync(Transaction tx, CancellationToken cancellation = default(CancellationToken))
		{
			return SendAsync<BroadcastResult>(HttpMethod.Post, tx.ToBytes(), "v1/broadcast", null, cancellation);
		}

		private static readonly HttpClient SharedClient = new HttpClient();
		internal HttpClient Client = SharedClient;

		private readonly NetworkInformation _NetworkInformation;
		public Network Network
		{
			get
			{
				return _NetworkInformation.Network;
			}
		}


		private readonly Uri _Address;
		public Uri Address
		{
			get
			{
				return _Address;
			}
		}


		internal string GetFullUri(string relativePath, params object[] parameters)
		{
			relativePath = String.Format(relativePath, parameters ?? new object[0]);
			var uri = Address.AbsoluteUri;
			if(!uri.EndsWith("/", StringComparison.Ordinal))
				uri += "/";
			uri += relativePath;
			return uri;
		}
		private Task<T> GetAsync<T>(string relativePath, object[] parameters, CancellationToken cancellation)
		{
			return SendAsync<T>(HttpMethod.Get, null, relativePath, parameters, cancellation);
		}
		private async Task<T> SendAsync<T>(HttpMethod method, object body, string relativePath, object[] parameters, CancellationToken cancellation)
		{
			HttpRequestMessage message = CreateMessage(method, body, relativePath, parameters);
			var result = await Client.SendAsync(message, cancellation).ConfigureAwait(false);
			if((int)result.StatusCode == 404)
			{
				return default(T);
			}
			if((int)result.StatusCode == 401)
			{
				if(_Auth.RefreshCache())
				{
					message = CreateMessage(method, body, relativePath, parameters);
					result = await Client.SendAsync(message).ConfigureAwait(false);
				}
			}
			return await ParseResponse<T>(result).ConfigureAwait(false);
		}

		internal HttpRequestMessage CreateMessage(HttpMethod method, object body, string relativePath, object[] parameters)
		{
			var uri = GetFullUri(relativePath, parameters);
			var message = new HttpRequestMessage(method, uri);
			_Auth.SetAuthorization(message);
			if(body != null)
			{
				if(body is byte[])
					message.Content = new ByteArrayContent((byte[])body);
				else
					message.Content = new StringContent(_Serializer.ToString(body), Encoding.UTF8, "application/json");
			}

			return message;
		}

		private async Task<T> ParseResponse<T>(HttpResponseMessage response)
		{
			using(response)
			{
				if(response.IsSuccessStatusCode)
					if(response.Content.Headers.ContentLength == 0)
						return default(T);
					else if(response.Content.Headers.ContentType.MediaType.Equals("application/json", StringComparison.Ordinal))
						return _Serializer.ToObject<T>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
					else if(response.Content.Headers.ContentType.MediaType.Equals("application/octet-stream", StringComparison.Ordinal))
					{
						return (T)(object)await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
					}
				if(response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
					response.EnsureSuccessStatusCode();
				var error = _Serializer.ToObject<NBXplorerError>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
				if(error == null)
					response.EnsureSuccessStatusCode();
				throw error.AsException();
			}
		}

		private async Task ParseResponse(HttpResponseMessage response)
		{
			using(response)
			{
				if(response.IsSuccessStatusCode)
					return;
				if(response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
					response.EnsureSuccessStatusCode();
				var error = _Serializer.ToObject<NBXplorerError>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
				if(error == null)
					response.EnsureSuccessStatusCode();
				throw error.AsException();
			}
		}
	}
}
