using NBitcoin;
using Newtonsoft.Json.Linq;
using NBitcoin.RPC;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBXplorer.Models;
using NBitcoin.DataEncoders;

namespace NBXplorer
{
	public class GetBlockchainInfoResponse
	{
		[JsonProperty("headers")]
		public int Headers
		{
			get; set;
		}
		[JsonProperty("blocks")]
		public int Blocks
		{
			get; set;
		}
		[JsonProperty("verificationprogress")]
		public double VerificationProgress
		{
			get; set;
		}

		[JsonProperty("mediantime")]
		public long? MedianTime
		{
			get; set;
		}

		[JsonProperty("initialblockdownload")]
		public bool? InitialBlockDownload
		{
			get; set;
		}
	}

	public class GetNetworkInfoResponse
	{
		public class LocalAddress
		{
			public string address { get; set; }
			public int port { get; set; }
		}
		public double? relayfee
		{
			get; set;
		}
		public FeeRate GetRelayFee()
		{
			return relayfee == null ? null : new FeeRate(Money.Coins((decimal)relayfee), 1000);
		}
		public double? incrementalfee
		{
			get; set;
		}
		public FeeRate GetIncrementalFee()
		{
			return incrementalfee == null ? null : new FeeRate(Money.Coins((decimal)incrementalfee), 1000);
		}
		public LocalAddress[] localaddresses
		{
			get; set;
		}
	}

	public static class RPCClientExtensions
    {
		public static async Task<GetBlockchainInfoResponse> GetBlockchainInfoAsyncEx(this RPCClient client)
		{
			var result = await client.SendCommandAsync("getblockchaininfo").ConfigureAwait(false);
			return JsonConvert.DeserializeObject<GetBlockchainInfoResponse>(result.ResultString);
		}

		public static async Task<GetNetworkInfoResponse> GetNetworkInfoAsync(this RPCClient client)
		{
			var result = await client.SendCommandAsync("getnetworkinfo").ConfigureAwait(false);
			return JsonConvert.DeserializeObject<GetNetworkInfoResponse>(result.ResultString);
		}

		public static async Task<Repository.SavedTransaction> TryGetTransaction(this RPCClient client, uint256 txId)
		{
			var request = new RPCRequest(RPCOperations.getrawtransaction, new object[] { txId, true });
			var response = await client.SendCommandAsync(request, false);
			if (response.Error == null && response.Result is JToken rpcResult && rpcResult["hex"] != null)
			{
				uint256 blockHash = null;
				if (rpcResult["blockhash"] != null)
				{
					blockHash = uint256.Parse(rpcResult.Value<string>("blockhash"));
				}
				DateTimeOffset timestamp = DateTimeOffset.UtcNow;
				if (rpcResult["time"] != null)
				{
					timestamp = NBitcoin.Utils.UnixTimeToDateTime(rpcResult.Value<long>("time"));
				}
				
				var rawTx = client.Network.Consensus.ConsensusFactory.CreateTransaction();
				rawTx.ReadWrite(Encoders.Hex.DecodeData(rpcResult.Value<string>("hex")), client.Network);
				return new Repository.SavedTransaction()
									{
										BlockHash = blockHash,
										Timestamp = timestamp,
										Transaction = rawTx
									};
			}
			return null;
		}
	}
}
