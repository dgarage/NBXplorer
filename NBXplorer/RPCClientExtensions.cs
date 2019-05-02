using NBitcoin;
using Newtonsoft.Json.Linq;
using NBitcoin.RPC;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBXplorer.Models;

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
		public static async Task<EstimateSmartFeeResponse> TryEstimateSmartFeeAsyncEx(this RPCClient client, int confirmationTarget, EstimateSmartFeeMode estimateMode = EstimateSmartFeeMode.Conservative)
		{
			// BCH removed estimatesmartfee and removed arguments to estimatefee so special case for them...
			if (client.Network.NetworkSet.CryptoCode == "BCH") 
			{
				var response = await client.SendCommandAsync(RPCOperations.estimatefee).ConfigureAwait(false);
				var result = response.Result.Value<decimal>();
				var money = Money.Coins(result);
				if (money.Satoshi < 0)
					return null;
				return new EstimateSmartFeeResponse() { FeeRate = new FeeRate(money), Blocks = confirmationTarget };
			}
			else
			{
				return await client.TryEstimateSmartFeeAsync(confirmationTarget, estimateMode);
			}
		}
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
	}
}
