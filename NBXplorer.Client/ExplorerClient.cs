﻿using NBitcoin;
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
				if(_CachedAuth != null)
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
			if(network == null)
				throw new ArgumentNullException(nameof(network));
			_Address = serverAddress;
			_Network = network;
			_Serializer = new Serializer(network.NBitcoinNetwork);
			_CryptoCode = _Network.CryptoCode;
			_Factory = new DerivationStrategy.DerivationStrategyFactory(Network.NBitcoinNetwork);
			SetCookieAuth(network.DefaultSettings.DefaultCookieFile);
		}

		internal IAuth _Auth = new NullAuthentication();

		public bool SetCookieAuth(string path)
		{
			if(path == null)
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

		Serializer _Serializer;
		DerivationStrategy.DerivationStrategyFactory _Factory;
		public UTXOChanges GetUTXOs(DerivationStrategyBase extKey, UTXOChanges previousChange, bool longPolling = true, CancellationToken cancellation = default(CancellationToken))
		{
			return GetUTXOsAsync(extKey, previousChange, longPolling, cancellation).GetAwaiter().GetResult();
		}

		public async Task<TransactionResult> GetTransactionAsync(uint256 txId, CancellationToken cancellation = default(CancellationToken))
		{
			return await SendAsync<TransactionResult>(HttpMethod.Get, null, "v1/cryptos/{0}/transactions/" + txId, new[] { CryptoCode }, cancellation).ConfigureAwait(false);
		}

		public TransactionResult GetTransaction(uint256 txId, CancellationToken cancellation = default(CancellationToken))
		{
			return GetTransactionAsync(txId, cancellation).GetAwaiter().GetResult();
		}

		public Task<UTXOChanges> GetUTXOsAsync(DerivationStrategyBase extKey, UTXOChanges previousChange, bool longPolling = true, CancellationToken cancellation = default(CancellationToken))
		{
			return GetUTXOsAsync(extKey, previousChange?.Confirmed?.Bookmark, previousChange?.Unconfirmed?.Bookmark, longPolling, cancellation);
		}

		public UTXOChanges GetUTXOs(DerivationStrategyBase extKey, Bookmark confirmedBookmark, Bookmark unconfirmedBookmark, bool longPolling = true, CancellationToken cancellation = default(CancellationToken))
		{
			return GetUTXOsAsync(extKey, confirmedBookmark, unconfirmedBookmark, longPolling, cancellation).GetAwaiter().GetResult();
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

		public Task<UTXOChanges> GetUTXOsAsync(DerivationStrategyBase extKey, Bookmark confirmedBookmark, Bookmark unconfirmedBookmark, bool longPolling = true, CancellationToken cancellation = default(CancellationToken))
		{
			return GetUTXOsAsync(extKey,
				confirmedBookmark == null ? null as Bookmark[] : new Bookmark[] { confirmedBookmark },
				unconfirmedBookmark == null ? null as Bookmark[] : new Bookmark[] { unconfirmedBookmark }, longPolling, cancellation);
		}

		public async Task<UTXOChanges> GetUTXOsAsync(DerivationStrategyBase extKey, Bookmark[] confirmedBookmarks, Bookmark[] unconfirmedBookmarks, bool longPolling = true, CancellationToken cancellation = default(CancellationToken))
		{
			Dictionary<string, string> parameters = new Dictionary<string, string>();
			if(confirmedBookmarks != null)
				parameters.Add("confirmedBookmarks", String.Join(",", confirmedBookmarks.Select(b => b.ToString())));
			if(unconfirmedBookmarks != null)
				parameters.Add("unconfirmedBookmarks", String.Join(",", unconfirmedBookmarks.Select(b => b.ToString())));
			parameters.Add("longPolling", longPolling.ToString());

			var query = String.Join("&", parameters.Select(p => p.Key + "=" + p.Value).ToArray());
			return await SendAsync<UTXOChanges>(HttpMethod.Get, null, "v1/cryptos/{0}/derivations/{1}/utxos?" + query, new object[] { CryptoCode, extKey.ToString() }, cancellation).ConfigureAwait(false);
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
			return SendAsync<string>(HttpMethod.Post, null, "v1/cryptos/{0}/derivations/{1}", new[] { CryptoCode, strategy.ToString() }, cancellation);
		}

		public void CancelReservation(DerivationStrategyBase strategy, KeyPath[] keyPaths, CancellationToken cancellation = default(CancellationToken))
		{
			CancelReservationAsync(strategy, keyPaths, cancellation).GetAwaiter().GetResult();
		}

		public Task CancelReservationAsync(DerivationStrategyBase strategy, KeyPath[] keyPaths, CancellationToken cancellation = default(CancellationToken))
		{
			return SendAsync<string>(HttpMethod.Post, keyPaths, "v1/cryptos/{0}/derivations/{1}/addresses/cancelreservation", new[] { CryptoCode, strategy.ToString() }, cancellation);
		}

		public StatusResult GetStatus(CancellationToken cancellation = default(CancellationToken))
		{
			return GetStatusAsync(cancellation).GetAwaiter().GetResult();
		}

		public Task<StatusResult> GetStatusAsync(CancellationToken cancellation = default(CancellationToken))
		{
			return SendAsync<StatusResult>(HttpMethod.Get, null, $"v1/cryptos/{CryptoCode}/status", null, cancellation);
		}
		public GetTransactionsResponse GetTransactions(DerivationStrategyBase strategy, GetTransactionsResponse previous, bool longPolling = true, CancellationToken cancellation = default(CancellationToken))
		{
			return GetTransactionsAsync(strategy, previous, longPolling, cancellation).GetAwaiter().GetResult();
		}
		public GetTransactionsResponse GetTransactions(DerivationStrategyBase strategy, Bookmark[] confirmedBookmarks, Bookmark[] unconfirmedBookmarks, Bookmark[] replacedBookmarks, bool longPolling = true, CancellationToken cancellation = default(CancellationToken))
		{
			return GetTransactionsAsync(strategy, confirmedBookmarks, unconfirmedBookmarks, replacedBookmarks, longPolling, cancellation).GetAwaiter().GetResult();
		}
		public Task<GetTransactionsResponse> GetTransactionsAsync(DerivationStrategyBase strategy, GetTransactionsResponse previous, bool longPolling, CancellationToken cancellation = default(CancellationToken))
		{
			return GetTransactionsAsync(strategy,
										previous == null ? null : new[] { previous.ConfirmedTransactions.Bookmark },
										previous == null ? null : new[] { previous.UnconfirmedTransactions.Bookmark }, 
										previous == null ? null : new[] { previous.ReplacedTransactions.Bookmark }, longPolling, cancellation);
		}
		public Task<GetTransactionsResponse> GetTransactionsAsync(DerivationStrategyBase strategy, Bookmark[] confirmedBookmarks, Bookmark[] unconfirmedBookmarks, Bookmark[] replacedBookmarks, bool longPolling, CancellationToken cancellation = default(CancellationToken))
		{
			Dictionary<string, string> parameters = new Dictionary<string, string>();
			if(confirmedBookmarks != null)
				parameters.Add("confirmedBookmarks", String.Join(",", confirmedBookmarks.Select(b => b.ToString())));
			if(unconfirmedBookmarks != null)
				parameters.Add("unconfirmedBookmarks", String.Join(",", unconfirmedBookmarks.Select(b => b.ToString())));
			if(replacedBookmarks != null)
				parameters.Add("replacedBookmarks", String.Join(",", replacedBookmarks.Select(b => b.ToString())));
			parameters.Add("longPolling", longPolling.ToString());
			var query = String.Join("&", parameters.Select(p => p.Key + "=" + p.Value).ToArray());
			return SendAsync<GetTransactionsResponse>(HttpMethod.Get, null, $"v1/cryptos/{CryptoCode}/derivations/{strategy}/transactions?" + query, null, cancellation);
		}

		public KeyPathInformation GetUnused(DerivationStrategyBase strategy, DerivationFeature feature, int skip = 0, bool reserve = false, CancellationToken cancellation = default(CancellationToken))
		{
			return GetUnusedAsync(strategy, feature, skip, reserve, cancellation).GetAwaiter().GetResult();
		}

		public async Task<KeyPathInformation> GetUnusedAsync(DerivationStrategyBase strategy, DerivationFeature feature, int skip = 0, bool reserve = false, CancellationToken cancellation = default(CancellationToken))
		{
			try
			{
				return await GetAsync<KeyPathInformation>($"v1/cryptos/{CryptoCode}/derivations/{strategy}/addresses/unused?feature={feature}&skip={skip}&reserve={reserve}", null, cancellation).ConfigureAwait(false);
			}
			catch(NBXplorerException ex) when(ex.Error?.HttpCode == 404)
			{
				return null;
			}
		}
		
		public async Task<KeyPathInformation[]> GetKeyInformationsAsync(Script script, CancellationToken cancellation = default(CancellationToken))
		{
			return await SendAsync<KeyPathInformation[]>(HttpMethod.Get, null, "v1/cryptos/{0}/scripts/" + script.ToHex(), new[] { CryptoCode }, cancellation).ConfigureAwait(false);
		}

		public KeyPathInformation[] GetKeyInformations(Script script, CancellationToken cancellation = default(CancellationToken))
		{
			return GetKeyInformationsAsync(script, cancellation).GetAwaiter().GetResult();
		}

		public GetFeeRateResult GetFeeRate(int blockCount, CancellationToken cancellation = default(CancellationToken))
		{
			return GetFeeRateAsync(blockCount, cancellation).GetAwaiter().GetResult();
		}

		public GetFeeRateResult GetFeeRate(int blockCount, FeeRate fallbackFeeRate, CancellationToken cancellation = default(CancellationToken))
		{
			return GetFeeRateAsync(blockCount, fallbackFeeRate, cancellation).GetAwaiter().GetResult();
		}
		public async Task<GetFeeRateResult> GetFeeRateAsync(int blockCount, FeeRate fallbackFeeRate, CancellationToken cancellation = default(CancellationToken))
		{
			try
			{
				return await GetAsync<GetFeeRateResult>("v1/cryptos/{0}/fees/{1}", new object[] { CryptoCode, blockCount }, cancellation).ConfigureAwait(false);
			}
			catch(NBXplorerException ex) when (fallbackFeeRate != null && ex.Error.Code == "fee-estimation-unavailable")
			{
				return new GetFeeRateResult() { BlockCount = blockCount, FeeRate = fallbackFeeRate };
			}
		}
		public Task<GetFeeRateResult> GetFeeRateAsync(int blockCount, CancellationToken cancellation = default(CancellationToken))
		{
			return GetAsync<GetFeeRateResult>("v1/cryptos/{0}/fees/{1}", new object[] { CryptoCode, blockCount }, cancellation);
		}

		public BroadcastResult Broadcast(Transaction tx, CancellationToken cancellation = default(CancellationToken))
		{
			return BroadcastAsync(tx, cancellation).GetAwaiter().GetResult();
		}

		public Task<BroadcastResult> BroadcastAsync(Transaction tx, CancellationToken cancellation = default(CancellationToken))
		{
			return SendAsync<BroadcastResult>(HttpMethod.Post, tx.ToBytes(), "v1/cryptos/{0}/transactions", new[] { CryptoCode }, cancellation);
		}

		private static readonly HttpClient SharedClient = new HttpClient();
		internal HttpClient Client = SharedClient;

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


		internal string GetFullUri(string relativePath, params object[] parameters)
		{
			relativePath = String.Format(relativePath, parameters ?? new object[0]);
			var uri = Address.AbsoluteUri;
			if(!uri.EndsWith("/", StringComparison.Ordinal))
				uri += "/";
			uri += relativePath;
			if(!IncludeTransaction)
			{
				if(uri.IndexOf('?') == -1)
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
