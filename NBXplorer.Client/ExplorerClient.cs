using NBitcoin;
using NBitcoin.JsonConverters;
using NBXplorer.Client.Models;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class ExplorerClient
	{
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
		}
		Serializer _Serializer;
		DerivationStrategyFactory _Factory;
		public UTXOChanges Sync(IDerivationStrategy extKey, UTXOChanges previousChange, bool noWait = false)
		{
			return SyncAsync(extKey, previousChange, noWait).GetAwaiter().GetResult();
		}

		public Task<UTXOChanges> SyncAsync(IDerivationStrategy extKey, UTXOChanges previousChange, bool noWait = false)
		{
			return SyncAsync(extKey, previousChange?.Confirmed?.Hash, previousChange?.Unconfirmed?.Hash, noWait);
		}

		public UTXOChanges Sync(IDerivationStrategy extKey, uint256 lastBlockHash, uint256 unconfirmedHash, bool noWait = false)
		{
			return SyncAsync(extKey, lastBlockHash, unconfirmedHash, noWait).GetAwaiter().GetResult();
		}

		public async Task<UTXOChanges> SyncAsync(IDerivationStrategy extKey, uint256 confHash, uint256 unconfHash, bool noWait = false)
		{
			confHash = confHash ?? uint256.Zero;
			unconfHash = unconfHash ?? uint256.Zero;
			var bytes = await SendAsync<byte[]>(HttpMethod.Get, null, "v1/sync/{0}?confHash={1}&unconfHash={2}&noWait={3}", _Factory.Serialize(extKey), confHash, unconfHash, noWait).ConfigureAwait(false);
			UTXOChanges changes = new UTXOChanges();
			changes.FromBytes(bytes);
			return changes;
		}

		public KeyPathInformation GetUnused(IDerivationStrategy strategy, DerivationFeature feature, int skip = 0)
		{
			return GetUnusedAsync(strategy, feature, skip).GetAwaiter().GetResult();
		}

		public async Task<KeyPathInformation> GetUnusedAsync(IDerivationStrategy strategy, DerivationFeature feature, int skip = 0)
		{
			try
			{
				return await GetAsync<KeyPathInformation>("v1/addresses/{0}/unused?feature={1}&skip={2}", _Factory.Serialize(strategy), feature, skip).ConfigureAwait(false);
			}
			catch(NBXplorerException ex)
			{
				if(ex.Error.HttpCode == 404)
					return null;
				throw;
			}
		}

		public BroadcastResult Broadcast(Transaction tx)
		{
			return BroadcastAsync(tx).GetAwaiter().GetResult();
		}

		public Task<BroadcastResult> BroadcastAsync(Transaction tx)
		{
			return SendAsync<BroadcastResult>(HttpMethod.Post, tx.ToBytes(), "v1/broadcast");
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
		private Task<T> GetAsync<T>(string relativePath, params object[] parameters)
		{
			return SendAsync<T>(HttpMethod.Get, null, relativePath, parameters);
		}
		private async Task<T> SendAsync<T>(HttpMethod method, object body, string relativePath, params object[] parameters)
		{
			var uri = GetFullUri(relativePath, parameters);
			var message = new HttpRequestMessage(method, uri);
			if(body != null)
			{
				if(body is byte[])
					message.Content = new ByteArrayContent((byte[])body);
				else
					message.Content = new StringContent(_Serializer.ToString(body), Encoding.UTF8, "application/json");
			}
			var result = await Client.SendAsync(message).ConfigureAwait(false);
			return await ParseResponse<T>(result).ConfigureAwait(false);
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
