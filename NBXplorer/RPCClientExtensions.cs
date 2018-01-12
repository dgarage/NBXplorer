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
    public static class RPCClientExtensions
    {
		public static async Task<GetBlockchainInfoResponse> GetBlockchainInfoAsync(this RPCClient client)
		{
			var result = await client.SendCommandAsync("getblockchaininfo").ConfigureAwait(false);
			return JsonConvert.DeserializeObject<GetBlockchainInfoResponse>(result.ResultString);
		}
    }
}
