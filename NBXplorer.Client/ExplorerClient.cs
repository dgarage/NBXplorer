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
using System.Net.WebSockets;
using Newtonsoft.Json.Linq;

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
				if (_CachedAuth != null)
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

		public ExplorerClient(NBXplorerNetwork network, Uri serverAddress = null)
		{
			serverAddress = serverAddress ?? network.DefaultSettings.DefaultUrl;
			if (network == null)
				throw new ArgumentNullException(nameof(network));
			_Address = serverAddress;
			_Network = network;
			Serializer = new Serializer(network.NBitcoinNetwork);
			_CryptoCode = _Network.CryptoCode;
			_Factory = new DerivationStrategy.DerivationStrategyFactory(Network.NBitcoinNetwork);
			SetCookieAuth(network.DefaultSettings.DefaultCookieFile);
		}

		internal IAuth _Auth = new NullAuthentication();

		public bool SetCookieAuth(string path)
		{
			if (path == null)
				throw new ArgumentNullException(nameof(path));
			CookieAuthentication auth = new CookieAuthentication(path);
			_Auth = auth;
			return auth.RefreshCache();
		}

		public void SetNoAuth()
		{
			_Auth = new NullAuthentication();
		}

		private readonly string _CryptoCode = "BTC";
		public string CryptoCode
		{
			get
			{
				return _CryptoCode;
			}
		}

		DerivationStrategy.DerivationStrategyFactory _Factory;
		public UTXOChanges GetUTXOs(DerivationStrategyBase extKey, CancellationToken cancellation = default)
		{
			return GetUTXOsAsync(extKey, cancellation).GetAwaiter().GetResult();
		}
		public Task<UTXOChanges> GetUTXOsAsync(DerivationStrategyBase extKey, CancellationToken cancellation = default)
		{
			if (extKey == null)
				throw new ArgumentNullException(nameof(extKey));
			return GetUTXOsAsync(TrackedSource.Create(extKey, Network.NBitcoinNetwork), cancellation);
		}

		public async Task<TransactionResult> GetTransactionAsync(uint256 txId, CancellationToken cancellation = default)
		{
			return await SendAsync<TransactionResult>(HttpMethod.Get, null, "v1/cryptos/{0}/transactions/" + txId, new[] { CryptoCode }, cancellation).ConfigureAwait(false);
		}

		public TransactionResult GetTransaction(uint256 txId, CancellationToken cancellation = default)
		{
			return GetTransactionAsync(txId, cancellation).GetAwaiter().GetResult();
		}

		public async Task ScanUTXOSetAsync(DerivationStrategyBase extKey, int? batchSize = null, int? gapLimit = null, int? fromIndex = null, CancellationToken cancellation = default)
		{
			if (extKey == null)
				throw new ArgumentNullException(nameof(extKey));
			List<string> args = new List<string>();
			if (batchSize != null)
				args.Add($"batchsize={batchSize.Value}");
			if (gapLimit != null)
				args.Add($"gaplimit={gapLimit.Value}");
			if (fromIndex != null)
				args.Add($"from={fromIndex.Value}");
			var argsString = string.Join("&", args.ToArray());
			if (argsString != string.Empty)
				argsString = $"?{argsString}";
			await SendAsync<bool>(HttpMethod.Post, null, "v1/cryptos/{0}/derivations/{1}/utxos/scan{2}", new object[] { Network.CryptoCode, extKey, argsString }, cancellation).ConfigureAwait(false);
		}
		public void ScanUTXOSet(DerivationStrategyBase extKey, int? batchSize = null, int? gapLimit = null, int? fromIndex = null, CancellationToken cancellation = default)
		{
			ScanUTXOSetAsync(extKey, batchSize, gapLimit, fromIndex, cancellation).GetAwaiter().GetResult();
		}

		public async Task<ScanUTXOInformation> GetScanUTXOSetInformationAsync(DerivationStrategyBase extKey, CancellationToken cancellation = default)
		{
			return await SendAsync<ScanUTXOInformation>(HttpMethod.Get, null, "v1/cryptos/{0}/derivations/{1}/utxos/scan", new object[] { Network.CryptoCode, extKey }, cancellation).ConfigureAwait(false);
		}

		public ScanUTXOInformation GetScanUTXOSetInformation(DerivationStrategyBase extKey, CancellationToken cancellation = default)
		{
			return GetScanUTXOSetInformationAsync(extKey, cancellation).GetAwaiter().GetResult();
		}

		public LongPollingNotificationSession CreateLongPollingNotificationSession(long lastEventId = 0)
		{
			return new LongPollingNotificationSession(lastEventId, this);
		}

		public WebsocketNotificationSession CreateWebsocketNotificationSession(CancellationToken cancellation = default)
		{
			return CreateWebsocketNotificationSessionAsync(cancellation).GetAwaiter().GetResult();
		}

		public async Task<WebsocketNotificationSession> CreateWebsocketNotificationSessionAsync(CancellationToken cancellation = default)
		{
			var session = new WebsocketNotificationSession(this);
			await session.ConnectAsync(cancellation).ConfigureAwait(false);
			return session;
		}

		public UTXOChanges GetUTXOs(TrackedSource trackedSource, CancellationToken cancellation = default)
		{
			return GetUTXOsAsync(trackedSource, cancellation).GetAwaiter().GetResult();
		}
		public async Task<UTXOChanges> GetUTXOsAsync(TrackedSource trackedSource, CancellationToken cancellation = default)
		{
			if (trackedSource == null)
				throw new ArgumentNullException(nameof(trackedSource));
			if (trackedSource is DerivationSchemeTrackedSource dsts)
			{
				return await SendAsync<UTXOChanges>(HttpMethod.Get, null, "v1/cryptos/{0}/derivations/{1}/utxos", new object[] { CryptoCode, dsts.DerivationStrategy.ToString() }, cancellation).ConfigureAwait(false);
			}
			else if (trackedSource is AddressTrackedSource asts)
			{
				return await SendAsync<UTXOChanges>(HttpMethod.Get, null, "v1/cryptos/{0}/addresses/{1}/utxos", new object[] { CryptoCode, asts.Address }, cancellation).ConfigureAwait(false);
			}
			else
				throw UnSupported(trackedSource);
		}

		public void WaitServerStarted(CancellationToken cancellation = default)
		{
			WaitServerStartedAsync(cancellation).GetAwaiter().GetResult();
		}
		public async Task WaitServerStartedAsync(CancellationToken cancellation = default)
		{
			while (true)
			{
				try
				{
					var status = await GetStatusAsync(cancellation).ConfigureAwait(false);
					if (status.IsFullySynched)
						break;
					await Task.Delay(10);
				}
				catch (OperationCanceledException) { throw; }
				catch { }
				cancellation.ThrowIfCancellationRequested();
			}
		}


		public void Track(DerivationStrategyBase strategy, CancellationToken cancellation = default)
		{
			TrackAsync(strategy, cancellation).GetAwaiter().GetResult();
		}
		public Task TrackAsync(DerivationStrategyBase strategy, CancellationToken cancellation = default)
		{
			return TrackAsync(TrackedSource.Create(strategy, Network.NBitcoinNetwork), cancellation);
		}

		public void Track(TrackedSource trackedSource, CancellationToken cancellation = default)
		{
			TrackAsync(trackedSource, cancellation).GetAwaiter().GetResult();
		}
		public Task TrackAsync(TrackedSource trackedSource, CancellationToken cancellation = default)
		{
			if (trackedSource == null)
				throw new ArgumentNullException(nameof(trackedSource));
			if (trackedSource is DerivationSchemeTrackedSource dsts)
			{
				return SendAsync<string>(HttpMethod.Post, null, "v1/cryptos/{0}/derivations/{1}", new[] { CryptoCode, dsts.DerivationStrategy.ToString() }, cancellation);
			}
			else if (trackedSource is AddressTrackedSource asts)
			{
				return SendAsync<string>(HttpMethod.Post, null, "v1/cryptos/{0}/addresses/{1}", new[] { CryptoCode, asts.Address.ToString() }, cancellation);
			}
			else
				throw UnSupported(trackedSource);
		}

		private Exception UnSupported(TrackedSource trackedSource)
		{
			return new NotSupportedException($"Unsupported {trackedSource.GetType().Name}");
		}

		public async Task<GetEventsResponse> GetEventsAsync(long lastEventId = 0, int? limit = null, bool longPolling = false, CancellationToken cancellation = default)
		{
			List<string> parameters = new List<string>();
			if (lastEventId != 0)
				parameters.Add($"lastEventId={lastEventId}");
			if (limit != null)
				parameters.Add($"limit={limit.Value}");
			if (longPolling)
				parameters.Add($"longPolling={longPolling}");
			var parametersString = parameters.Count == 0 ? string.Empty : $"?{String.Join("&", parameters.ToArray<object>())}";
			var evts = await SendAsync<JObject>(HttpMethod.Get, null, $"v1/cryptos/{CryptoCode}/events{parametersString}", null, cancellation);
			var jsonSerializer = new Newtonsoft.Json.JsonSerializerSettings();
			Serializer.ConfigureSerializer(jsonSerializer);

			var resp = new GetEventsResponse();
			resp.LastEventId = evts["lastEventId"].Value<long>();
			resp.Events = ((JArray)evts["events"])
				  .Select(ev => NewEventBase.ParseEvent((JObject)ev, jsonSerializer))
				  .OfType<NewEventBase>()
				  .ToArray();
			return resp;
		}
		public GetEventsResponse GetEvents(long lastEventId = 0, int? limit = null, bool longPolling = false, CancellationToken cancellation = default)
		{
			return GetEventsAsync(lastEventId, limit, longPolling, cancellation).GetAwaiter().GetResult();
		}

		public void CancelReservation(DerivationStrategyBase strategy, KeyPath[] keyPaths, CancellationToken cancellation = default)
		{
			CancelReservationAsync(strategy, keyPaths, cancellation).GetAwaiter().GetResult();
		}

		public Task CancelReservationAsync(DerivationStrategyBase strategy, KeyPath[] keyPaths, CancellationToken cancellation = default)
		{
			return SendAsync<string>(HttpMethod.Post, keyPaths, "v1/cryptos/{0}/derivations/{1}/addresses/cancelreservation", new[] { CryptoCode, strategy.ToString() }, cancellation);
		}

		public GetBalanceResponse GetBalance(DerivationStrategyBase strategy, CancellationToken cancellation = default)
		{
			return GetBalanceAsync(strategy, cancellation).GetAwaiter().GetResult();
		}

		public Task<GetBalanceResponse> GetBalanceAsync(DerivationStrategyBase strategy, CancellationToken cancellation = default)
		{
			return SendAsync<GetBalanceResponse>(HttpMethod.Get, null, $"v1/cryptos/{CryptoCode}/derivations/{strategy}/balances", null, cancellation);
		}

		public StatusResult GetStatus(CancellationToken cancellation = default)
		{
			return GetStatusAsync(cancellation).GetAwaiter().GetResult();
		}

		public Task<StatusResult> GetStatusAsync(CancellationToken cancellation = default)
		{
			return SendAsync<StatusResult>(HttpMethod.Get, null, $"v1/cryptos/{CryptoCode}/status", null, cancellation);
		}
		public GetTransactionsResponse GetTransactions(DerivationStrategyBase strategy, CancellationToken cancellation = default)
		{
			return GetTransactionsAsync(strategy, cancellation).GetAwaiter().GetResult();
		}
		public GetTransactionsResponse GetTransactions(TrackedSource trackedSource, CancellationToken cancellation = default)
		{
			return GetTransactionsAsync(trackedSource, cancellation).GetAwaiter().GetResult();
		}

		public Task<GetTransactionsResponse> GetTransactionsAsync(DerivationStrategyBase strategy, CancellationToken cancellation = default)
		{
			return GetTransactionsAsync(TrackedSource.Create(strategy, Network.NBitcoinNetwork), cancellation);
		}
		public Task<GetTransactionsResponse> GetTransactionsAsync(TrackedSource trackedSource, CancellationToken cancellation = default)
		{
			if (trackedSource == null)
				throw new ArgumentNullException(nameof(trackedSource));
			if (trackedSource is DerivationSchemeTrackedSource dsts)
			{
				return SendAsync<GetTransactionsResponse>(HttpMethod.Get, null, $"v1/cryptos/{CryptoCode}/derivations/{dsts.DerivationStrategy}/transactions", null, cancellation);
			}
			else if (trackedSource is AddressTrackedSource asts)
			{
				return SendAsync<GetTransactionsResponse>(HttpMethod.Get, null, $"v1/cryptos/{CryptoCode}/addresses/{asts.Address}/transactions", null, cancellation);
			}
			else
				throw UnSupported(trackedSource);
		}


		public TransactionInformation GetTransaction(TrackedSource trackedSource, uint256 txId, CancellationToken cancellation = default)
		{
			return this.GetTransactionAsync(trackedSource, txId, cancellation).GetAwaiter().GetResult();
		}
		public TransactionInformation GetTransaction(DerivationStrategyBase derivationStrategyBase, uint256 txId, CancellationToken cancellation = default)
		{
			return this.GetTransactionAsync(derivationStrategyBase, txId, cancellation).GetAwaiter().GetResult();
		}
		public Task<TransactionInformation> GetTransactionAsync(DerivationStrategyBase derivationStrategyBase, uint256 txId, CancellationToken cancellation = default)
		{
			if (derivationStrategyBase == null)
				throw new ArgumentNullException(nameof(derivationStrategyBase));
			return GetTransactionAsync(new DerivationSchemeTrackedSource(derivationStrategyBase, Network.NBitcoinNetwork), txId, cancellation);
		}

		public Task<TransactionInformation> GetTransactionAsync(TrackedSource trackedSource, uint256 txId, CancellationToken cancellation = default)
		{
			if (txId == null)
				throw new ArgumentNullException(nameof(txId));
			if (trackedSource == null)
				throw new ArgumentNullException(nameof(trackedSource));
			if (trackedSource is DerivationSchemeTrackedSource dsts)
			{
				return SendAsync<TransactionInformation>(HttpMethod.Get, null, $"v1/cryptos/{CryptoCode}/derivations/{dsts.DerivationStrategy}/transactions/{txId}", null, cancellation);
			}
			else if (trackedSource is AddressTrackedSource asts)
			{
				return SendAsync<TransactionInformation>(HttpMethod.Get, null, $"v1/cryptos/{CryptoCode}/addresses/{asts.Address}/transactions/{txId}", null, cancellation);
			}
			else
				throw UnSupported(trackedSource);
		}

		public Task RescanAsync(RescanRequest rescanRequest, CancellationToken cancellation = default)
		{
			if (rescanRequest == null)
				throw new ArgumentNullException(nameof(rescanRequest));
			return SendAsync<byte[]>(HttpMethod.Post, rescanRequest, $"v1/cryptos/{CryptoCode}/rescan", null, cancellation);
		}

		public void Rescan(RescanRequest rescanRequest, CancellationToken cancellation = default)
		{
			RescanAsync(rescanRequest, cancellation).GetAwaiter().GetResult();
		}

		public KeyPathInformation GetKeyInformationFromKeyPath(DerivationStrategyBase strategy, KeyPath keyPath, CancellationToken cancellation = default(CancellationToken))
		{
			return GetKeyInformationFromKeyPathAsync(strategy, keyPath, cancellation).GetAwaiter().GetResult();
		}

		public Task<KeyPathInformation> GetKeyInformationFromKeyPathAsync(DerivationStrategyBase strategy, KeyPath keyPath, CancellationToken cancellation = default(CancellationToken))
		{
			return GetAsync<KeyPathInformation>($"v1/cryptos/{CryptoCode}/derivations/{strategy}/addresses?keyPath={keyPath}", null, cancellation);
		}

		public KeyPathInformation GetUnused(DerivationStrategyBase strategy, DerivationFeature feature, int skip = 0, bool reserve = false, CancellationToken cancellation = default)
		{
			return GetUnusedAsync(strategy, feature, skip, reserve, cancellation).GetAwaiter().GetResult();
		}

		public async Task<KeyPathInformation> GetUnusedAsync(DerivationStrategyBase strategy, DerivationFeature feature, int skip = 0, bool reserve = false, CancellationToken cancellation = default)
		{
			try
			{
				return await GetAsync<KeyPathInformation>($"v1/cryptos/{CryptoCode}/derivations/{strategy}/addresses/unused?feature={feature}&skip={skip}&reserve={reserve}", null, cancellation).ConfigureAwait(false);
			}
			catch (NBXplorerException ex) when (ex.Error?.HttpCode == 404)
			{
				return null;
			}
		}

		public LockUTXOsResponse LockUTXOs(DerivationStrategyBase derivationScheme, LockUTXOsRequest request, CancellationToken cancellation = default)
		{
			return LockUTXOsAsync(derivationScheme, request, cancellation).GetAwaiter().GetResult();
		}

		public Task<LockUTXOsResponse> LockUTXOsAsync(DerivationStrategyBase derivationScheme, LockUTXOsRequest request, CancellationToken cancellation = default)
		{
			return SendAsync<LockUTXOsResponse>(HttpMethod.Post, request, $"v1/cryptos/{CryptoCode}/derivations/{derivationScheme}/transactions", null, cancellation);
		}

		public bool UnlockUTXOs(string unlockId, CancellationToken cancellation = default)
		{
			return UnlockUTXOsAsync(unlockId, cancellation).GetAwaiter().GetResult();
		}

		public async Task<bool> UnlockUTXOsAsync(string unlockId, CancellationToken cancellation = default)
		{
			var relativePath = $"v1/cryptos/{CryptoCode}/locks/{unlockId}/cancel";
			HttpRequestMessage message = CreateMessage(HttpMethod.Post, null, relativePath, null);
			var result = await Client.SendAsync(message, cancellation).ConfigureAwait(false);
			if ((int)result.StatusCode == 404)
			{
				return false;
			}
			result.EnsureSuccessStatusCode();
			return true;
		}

		public KeyPathInformation GetKeyInformation(DerivationStrategyBase strategy, Script script, CancellationToken cancellation = default)
		{
			return GetKeyInformationAsync(strategy, script, cancellation).GetAwaiter().GetResult();
		}

		public async Task<KeyPathInformation> GetKeyInformationAsync(DerivationStrategyBase strategy, Script script, CancellationToken cancellation = default)
		{
			return await SendAsync<KeyPathInformation>(HttpMethod.Get, null, "v1/cryptos/{0}/derivations/{1}/scripts/" + script.ToHex(), new object[] { CryptoCode, strategy }, cancellation).ConfigureAwait(false);
		}

		[Obsolete("Use GetKeyInformationAsync(DerivationStrategyBase strategy, Script script) instead")]
		public async Task<KeyPathInformation[]> GetKeyInformationsAsync(Script script, CancellationToken cancellation = default)
		{
			return await SendAsync<KeyPathInformation[]>(HttpMethod.Get, null, "v1/cryptos/{0}/scripts/" + script.ToHex(), new[] { CryptoCode }, cancellation).ConfigureAwait(false);
		}

		[Obsolete("Use GetKeyInformation(DerivationStrategyBase strategy, Script script) instead")]
		public KeyPathInformation[] GetKeyInformations(Script script, CancellationToken cancellation = default)
		{
			return GetKeyInformationsAsync(script, cancellation).GetAwaiter().GetResult();
		}

		public GetFeeRateResult GetFeeRate(int blockCount, CancellationToken cancellation = default)
		{
			return GetFeeRateAsync(blockCount, cancellation).GetAwaiter().GetResult();
		}

		public GetFeeRateResult GetFeeRate(int blockCount, FeeRate fallbackFeeRate, CancellationToken cancellation = default)
		{
			return GetFeeRateAsync(blockCount, fallbackFeeRate, cancellation).GetAwaiter().GetResult();
		}
		public async Task<GetFeeRateResult> GetFeeRateAsync(int blockCount, FeeRate fallbackFeeRate, CancellationToken cancellation = default)
		{
			try
			{
				return await GetAsync<GetFeeRateResult>("v1/cryptos/{0}/fees/{1}", new object[] { CryptoCode, blockCount }, cancellation).ConfigureAwait(false);
			}
			catch (NBXplorerException ex) when (fallbackFeeRate != null && ex.Error.Code == "fee-estimation-unavailable")
			{
				return new GetFeeRateResult() { BlockCount = blockCount, FeeRate = fallbackFeeRate };
			}
		}
		public Task<GetFeeRateResult> GetFeeRateAsync(int blockCount, CancellationToken cancellation = default)
		{
			return GetAsync<GetFeeRateResult>("v1/cryptos/{0}/fees/{1}", new object[] { CryptoCode, blockCount }, cancellation);
		}
		public CreatePSBTResponse CreatePSBT(DerivationStrategyBase derivationStrategy, CreatePSBTRequest request, CancellationToken cancellation = default)
		{
			return CreatePSBTAsync(derivationStrategy, request, cancellation).GetAwaiter().GetResult();
		}
		public Task<CreatePSBTResponse> CreatePSBTAsync(DerivationStrategyBase derivationStrategy, CreatePSBTRequest request, CancellationToken cancellation = default)
		{
			if (derivationStrategy == null)
				throw new ArgumentNullException(nameof(derivationStrategy));
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			return this.SendAsync<CreatePSBTResponse>(HttpMethod.Post, request, "v1/cryptos/{0}/derivations/{1}/psbt/create", new object[] { CryptoCode, derivationStrategy }, cancellation);
		}

		public BroadcastResult Broadcast(Transaction tx, CancellationToken cancellation = default)
		{
			return BroadcastAsync(tx, cancellation).GetAwaiter().GetResult();
		}

		public Task<BroadcastResult> BroadcastAsync(Transaction tx, CancellationToken cancellation = default)
		{
			return SendAsync<BroadcastResult>(HttpMethod.Post, tx.ToBytes(), "v1/cryptos/{0}/transactions", new[] { CryptoCode }, cancellation);
		}

		private static readonly HttpClient SharedClient = new HttpClient();
		internal HttpClient Client = SharedClient;

		public void SetClient(HttpClient client)
		{
			Client = client;
		}

		private readonly NBXplorerNetwork _Network;
		public NBXplorerNetwork Network
		{
			get
			{
				return _Network;
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

		public bool IncludeTransaction
		{
			get; set;
		} = true;
		public Serializer Serializer { get; private set; }

		internal string GetFullUri(string relativePath, params object[] parameters)
		{
			relativePath = String.Format(relativePath, parameters ?? new object[0]);
			var uri = Address.AbsoluteUri;
			if (!uri.EndsWith("/", StringComparison.Ordinal))
				uri += "/";
			uri += relativePath;
			if (!IncludeTransaction)
			{
				if (uri.IndexOf('?') == -1)
					uri += $"?includeTransaction=false";
				else
					uri += $"&includeTransaction=false";
			}
			return uri;
		}
		private Task<T> GetAsync<T>(string relativePath, object[] parameters, CancellationToken cancellation)
		{
			return SendAsync<T>(HttpMethod.Get, null, relativePath, parameters, cancellation);
		}
		internal async Task<T> SendAsync<T>(HttpMethod method, object body, string relativePath, object[] parameters, CancellationToken cancellation)
		{
			HttpRequestMessage message = CreateMessage(method, body, relativePath, parameters);
			var result = await Client.SendAsync(message, cancellation).ConfigureAwait(false);
			if ((int)result.StatusCode == 404)
			{
				return default(T);
			}
			if(result.StatusCode == HttpStatusCode.GatewayTimeout || result.StatusCode == HttpStatusCode.RequestTimeout)
			{
				throw new HttpRequestException($"HTTP error {(int)result.StatusCode}", new TimeoutException());
			}
			if ((int)result.StatusCode == 401)
			{
				if (_Auth.RefreshCache())
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
			if (body != null)
			{
				if (body is byte[])
					message.Content = new ByteArrayContent((byte[])body);
				else
					message.Content = new StringContent(Serializer.ToString(body), Encoding.UTF8, "application/json");
			}

			return message;
		}

		private async Task<T> ParseResponse<T>(HttpResponseMessage response)
		{
			using (response)
			{
				if (response.IsSuccessStatusCode)
					if (response.Content.Headers.ContentLength == 0)
						return default(T);
					else if (response.Content.Headers.ContentType.MediaType.Equals("application/json", StringComparison.Ordinal))
						return Serializer.ToObject<T>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
					else if (response.Content.Headers.ContentType.MediaType.Equals("application/octet-stream", StringComparison.Ordinal))
					{
						return (T)(object)await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
					}
				if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
					response.EnsureSuccessStatusCode();
				var error = Serializer.ToObject<NBXplorerError>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
				if (error == null)
					response.EnsureSuccessStatusCode();
				throw error.AsException();
			}
		}

		private async Task ParseResponse(HttpResponseMessage response)
		{
			using (response)
			{
				if (response.IsSuccessStatusCode)
					return;
				if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
					response.EnsureSuccessStatusCode();
				var error = Serializer.ToObject<NBXplorerError>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
				if (error == null)
					response.EnsureSuccessStatusCode();
				throw error.AsException();
			}
		}
	}
}
