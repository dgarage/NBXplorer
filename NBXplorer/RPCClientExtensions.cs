using NBitcoin;
using Newtonsoft.Json.Linq;
using NBitcoin.RPC;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
		public double relayfee
		{
			get; set;
		}
		public double incrementalfee
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

		public static async Task<FeeRate> GetFeeRateAsyncEx(this RPCClient client, int blockCount)
		{
			FeeRate rate = null;
			try
			{
				rate = (await client.TryEstimateSmartFeeAsync(blockCount, EstimateSmartFeeMode.Conservative).ConfigureAwait(false))?.FeeRate;
			}
			catch (RPCException ex) when (ex.RPCCode == RPCErrorCode.RPC_METHOD_NOT_FOUND)
			{
				var response = await client.SendCommandAsync(RPCOperations.estimatefee, blockCount).ConfigureAwait(false);
				var result = response.Result.Value<decimal>();
				var money = Money.Coins(result);
				if (money.Satoshi < 0)
					return null;
				rate = new FeeRate(money);
			}
			return rate;
		}
	}
}
