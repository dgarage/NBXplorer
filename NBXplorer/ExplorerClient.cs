using NBitcoin;
using NBitcoin.JsonConverters;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ElementsExplorer
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
		}

		public UTXOChanges Sync(BitcoinExtPubKey extKey, UTXOChanges previousChange, bool noWait = false)
		{
			return SyncAsync(extKey, previousChange, noWait).GetAwaiter().GetResult();
		}

		public Task<UTXOChanges> SyncAsync(BitcoinExtPubKey extKey, UTXOChanges previousChange, bool noWait = false)
		{
			return SyncAsync(extKey, previousChange?.Confirmed?.Hash, previousChange?.Unconfirmed?.Hash, noWait);
		}

		public UTXOChanges Sync(BitcoinExtPubKey extKey, uint256 lastBlockHash, uint256 unconfirmedHash, bool noWait = false)
		{
			return SyncAsync(extKey, lastBlockHash, unconfirmedHash, noWait).GetAwaiter().GetResult();
		}

		public async Task<UTXOChanges> SyncAsync(BitcoinExtPubKey extKey, uint256 lastBlockHash, uint256 unconfirmedHash, bool noWait = false)
		{
			lastBlockHash = lastBlockHash ?? uint256.Zero;
			unconfirmedHash = unconfirmedHash ?? uint256.Zero;
			var bytes = await SendAsync<byte[]>(HttpMethod.Get, null, "v1/sync/{0}?lastBlockHash={1}&unconfirmedHash={2}&noWait={3}", extKey, lastBlockHash, unconfirmedHash, noWait).ConfigureAwait(false);
			UTXOChanges changes = new UTXOChanges();
			changes.FromBytes(bytes);
			return changes;
		}

		public bool Broadcast(Transaction tx)
		{
			return BroadcastAsync(tx).GetAwaiter().GetResult();
		}

		public Task<bool> BroadcastAsync(Transaction tx)
		{
			return SendAsync<bool>(HttpMethod.Post, tx.ToBytes(), "v1/broadcast");
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
					message.Content = new StringContent(Serializer.ToString(body, Network), Encoding.UTF8, "application/json");
			}
			var result = await Client.SendAsync(message).ConfigureAwait(false);
			if(result.StatusCode == HttpStatusCode.NotFound)
				return default(T);
			if(!result.IsSuccessStatusCode)
			{
				string error = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
				if(!string.IsNullOrEmpty(error))
				{
					throw new HttpRequestException(result.StatusCode + ": " + error);
				}
			}
			result.EnsureSuccessStatusCode();
			if(typeof(T) == typeof(byte[]))
				return (T)(object)await result.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
			var str = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
			if(typeof(T) == typeof(string))
				return (T)(object)str;
			return Serializer.ToObject<T>(str, Network);
		}
	}
}
