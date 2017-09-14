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

namespace NBXplorer
{
	public class ExplorerClient
	{
		interface IAuth
		{
			bool RefreshCache();
			void SetAuthorization(HttpRequestMessage message);
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
		}

		public ExplorerClient(Network network, Uri serverAddress)
		{
			if(serverAddress == null)
				throw new ArgumentNullException(nameof(serverAddress));
			if(network == null)
				throw new ArgumentNullException(nameof(network));
			_Address = serverAddress;
			_Network = network;
			_Serializer = new Serializer(network);
			_Factory = new DerivationStrategyFactory(Network);
			var auth = new CookieAuthentication(NBXplorer.Client.Utils.GetDefaultCookieFilePath(network));
			if(auth.RefreshCache())
				_Auth = auth;
		}

		IAuth _Auth = new NullAuthentication();

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
		DerivationStrategyFactory _Factory;
		public UTXOChanges Sync(IDerivationStrategy extKey, UTXOChanges previousChange, bool noWait = false, CancellationToken cancellation = default(CancellationToken))
		{
			return SyncAsync(extKey, previousChange, noWait, cancellation).GetAwaiter().GetResult();
		}

		public async Task<TransactionResult> GetTransactionAsync(uint256 txId, CancellationToken cancellation = default(CancellationToken))
		{
			var bytes = await SendAsync<byte[]>(HttpMethod.Get, null, "v1/tx/" + txId, null, cancellation).ConfigureAwait(false);
			if(bytes == null)
				return null;
			BitcoinStream bs = new BitcoinStream(bytes);
			TransactionResult result = null;
			bs.ReadWrite(ref result);
			return result;
		}

		public TransactionResult GetTransaction(uint256 txId, CancellationToken cancellation = default(CancellationToken))
		{
			return GetTransactionAsync(txId, cancellation).GetAwaiter().GetResult();
		}

		public Task<UTXOChanges> SyncAsync(IDerivationStrategy extKey, UTXOChanges previousChange, bool noWait = false, CancellationToken cancellation = default(CancellationToken))
		{
			return SyncAsync(extKey, previousChange?.Confirmed?.Hash, previousChange?.Unconfirmed?.Hash, noWait, cancellation);
		}

		public UTXOChanges Sync(IDerivationStrategy extKey, uint256 confHash, uint256 unconfirmedHash, bool noWait = false, CancellationToken cancellation = default(CancellationToken))
		{
			return SyncAsync(extKey, confHash, unconfirmedHash, noWait, cancellation).GetAwaiter().GetResult();
		}

		public async Task<UTXOChanges> SyncAsync(IDerivationStrategy extKey, uint256 confHash, uint256 unconfHash, bool noWait = false, CancellationToken cancellation = default(CancellationToken))
		{
			Dictionary<string, string> parameters = new Dictionary<string, string>();
			if(confHash != null)
				parameters.Add("confHash", confHash.ToString());
			if(unconfHash != null)
				parameters.Add("unconfHash", unconfHash.ToString());
			parameters.Add("noWait", noWait.ToString());

			var query = String.Join("&", parameters.Select(p => p.Key + "=" + p.Value).ToArray());
			var bytes = await SendAsync<byte[]>(HttpMethod.Get, null, "v1/sync/{0}?" + query, new object[] { _Factory.Serialize(extKey) }, cancellation).ConfigureAwait(false);
			UTXOChanges changes = new UTXOChanges();
			changes.FromBytes(bytes);
			return changes;
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

					var pong = await SendAsync<string>(HttpMethod.Get, null, "v1/ping", null, cancellation).ConfigureAwait(false);
					if(pong.Equals("pong", StringComparison.Ordinal))
						break;
				}
				catch(OperationCanceledException) { throw; }
				catch { }
				cancellation.ThrowIfCancellationRequested();
			}
		}

		public KeyPathInformation GetUnused(IDerivationStrategy strategy, DerivationFeature feature, int skip = 0, bool reserve = false, CancellationToken cancellation = default(CancellationToken))
		{
			return GetUnusedAsync(strategy, feature, skip, reserve, cancellation).GetAwaiter().GetResult();
		}

		public async Task<KeyPathInformation> GetUnusedAsync(IDerivationStrategy strategy, DerivationFeature feature, int skip = 0, bool reserve = false, CancellationToken cancellation = default(CancellationToken))
		{
			try
			{
				return await GetAsync<KeyPathInformation>("v1/addresses/{0}/unused?feature={1}&skip={2}&reserve={3}", new object[] { _Factory.Serialize(strategy), feature, skip, reserve }, cancellation).ConfigureAwait(false);
			}
			catch(NBXplorerException ex)
			{
				if(ex.Error.HttpCode == 404)
					return null;
				throw;
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

		private readonly Network _Network;
		public Network Network
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


		private string GetFullUri(string relativePath, params object[] parameters)
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
			var uri = GetFullUri(relativePath, parameters);
			HttpRequestMessage message = CreateMessage(method, body, uri);
			var result = await Client.SendAsync(message, cancellation).ConfigureAwait(false);
			if((int)result.StatusCode == 404)
			{
				return default(T);
			}
			if((int)result.StatusCode == 401)
			{
				if(_Auth.RefreshCache())
				{
					message = CreateMessage(method, body, uri);
					result = await Client.SendAsync(message).ConfigureAwait(false);
				}
			}
			return await ParseResponse<T>(result).ConfigureAwait(false);
		}

		private HttpRequestMessage CreateMessage(HttpMethod method, object body, string uri)
		{
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
			if(response.IsSuccessStatusCode)
				if(response.Content.Headers.ContentType.MediaType.Equals("application/json", StringComparison.Ordinal))
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

		private async Task ParseResponse(HttpResponseMessage response)
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
