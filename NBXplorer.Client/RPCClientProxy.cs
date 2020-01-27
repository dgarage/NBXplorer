using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;
using NBitcoin.RPC;

namespace NBXplorer
{
	public class RPCClientProxy : RPCClient
	{
		private readonly ExplorerClient _explorerClient;

		public RPCClientProxy(ExplorerClient explorerClient) : base(explorerClient.Network.NBitcoinNetwork)
		{
			_explorerClient = explorerClient;
		}

		internal RPCClientProxy(ExplorerClient explorerClient,
			ConcurrentQueue<Tuple<RPCRequest, TaskCompletionSource<RPCResponse>>> batchedRequests) : base(explorerClient
			.Network.NBitcoinNetwork)
		{
			_explorerClient = explorerClient;
			_BatchedRequests = batchedRequests;
		}

		public override RPCClient PrepareBatch()
		{
			return new RPCClientProxy(_explorerClient, new ConcurrentQueue<Tuple<RPCRequest, TaskCompletionSource<RPCResponse>>>())
			{
				Capabilities = Capabilities,
				RequestTimeout = RequestTimeout,
				_HttpClient = _HttpClient
			};
		}

		protected override HttpRequestMessage CreateWebRequest(string json)
		{
			return _explorerClient.CreateMessage(HttpMethod.Post, json, "v1/cryptos/{0}/rpc",
				new object[] {_explorerClient.CryptoCode});
		}

		public override RPCClient Clone()
		{
			return new RPCClientProxy(_explorerClient, _BatchedRequests)
			{
				Capabilities = Capabilities,
				RequestTimeout = RequestTimeout,
				_HttpClient = _HttpClient
			};
		}
	}
}