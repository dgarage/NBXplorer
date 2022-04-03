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
using System.Threading;
using Microsoft.Extensions.Logging;
using NBXplorer.Backends;

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
		public static async Task<bool> WarmupBlockchain(this RPCClient rpc, ILogger logger)
		{
			if (await rpc.GetBlockCountAsync() < rpc.Network.Consensus.CoinbaseMaturity)
			{
				logger.LogInformation($"Less than {rpc.Network.Consensus.CoinbaseMaturity} blocks, mining some block for regtest");
				await rpc.EnsureGenerateAsync(rpc.Network.Consensus.CoinbaseMaturity + 1);
				return true;
			}
			else
			{
				var hash = await rpc.GetBestBlockHashAsync();

				BlockHeader header = null;
				try
				{
					header = await rpc.GetBlockHeaderAsync(hash);
				}
				catch (RPCException ex) when (ex.RPCCode == RPCErrorCode.RPC_METHOD_NOT_FOUND)
				{
					header = (await rpc.GetBlockAsync(hash)).Header;
				}
				if ((DateTimeOffset.UtcNow - header.BlockTime) > TimeSpan.FromSeconds(24 * 60 * 60))
				{
					logger.LogInformation($"It has been a while nothing got mined on regtest... mining 10 blocks");
					await rpc.GenerateAsync(10);
					return true;
				}
				return false;
			}
		}
		public static bool IsWhitelisted(this PeerInfo peer)
		{
			if (peer is null)
				return false;
			if (peer.IsWhiteListed)
				return true;
			if (peer.Permissions.Contains("noban", StringComparer.OrdinalIgnoreCase))
				return true;
			return false;
		}
		public static bool IsSynching(this GetBlockchainInfoResponse blockchainInfo, NBXplorerNetwork network)
		{
			if (blockchainInfo.InitialBlockDownload == true)
				return true;
			if (blockchainInfo.MedianTime.HasValue && network.NBitcoinNetwork.ChainName != ChainName.Regtest)
			{
				var time = NBitcoin.Utils.UnixTimeToDateTime(blockchainInfo.MedianTime.Value);
				// 5 month diff? probably synching...
				if (DateTimeOffset.UtcNow - time > TimeSpan.FromDays(30 * 5))
				{
					return true;
				}
			}

			return blockchainInfo.Headers - blockchainInfo.Blocks > 6;
		}
		public static async Task<GetBlockchainInfoResponse> GetBlockchainInfoAsyncEx(this RPCClient client, CancellationToken cancellationToken = default)
		{
			var result = await client.SendCommandAsync("getblockchaininfo", cancellationToken).ConfigureAwait(false);
			return JsonConvert.DeserializeObject<GetBlockchainInfoResponse>(result.ResultString);
		}

		public static async Task<GetNetworkInfoResponse> GetNetworkInfoAsync(this RPCClient client)
		{
			var result = await client.SendCommandAsync("getnetworkinfo").ConfigureAwait(false);
			return JsonConvert.DeserializeObject<GetNetworkInfoResponse>(result.ResultString);
		}

		public static async Task<PSBT> UTXOUpdatePSBT(this RPCClient rpcClient, PSBT psbt)
		{
			if (psbt == null) throw new ArgumentNullException(nameof(psbt));
			var response = await rpcClient.SendCommandAsync("utxoupdatepsbt", new object[] { psbt.ToBase64() });
			response.ThrowIfError();
			if (response.Error == null && response.Result is JValue rpcResult && rpcResult.Value is string psbtStr)
			{
				return PSBT.Parse(psbtStr, psbt.Network);
			}

			throw new Exception("This should never happen");
		}

		public static async Task<SlimChainedBlock> GetBlockHeaderAsyncEx(this NBitcoin.RPC.RPCClient rpc, uint256 blk)
		{
			var header = await rpc.SendCommandAsync(new NBitcoin.RPC.RPCRequest("getblockheader", new[] { blk.ToString() })
			{
				ThrowIfRPCError = false
			});
			if (header.Result is null || header.Error is not null)
				return null;
			var response = header.Result;
			var confs = response["confirmations"].Value<long>();
			if (confs == -1)
				return null;

			var prev = response["previousblockhash"]?.Value<string>();
			return new SlimChainedBlock(blk, prev is null ? null : new uint256(prev), response["height"].Value<int>());
		}

		public static async Task<SavedTransaction> TryGetRawTransaction(this RPCClient client, uint256 txId)
		{
			var request = new RPCRequest(RPCOperations.getrawtransaction, new object[] { txId, true }) { ThrowIfRPCError = false };
			var response = await client.SendCommandAsync(request);
			if (response.Error == null && response.Result is JToken rpcResult && rpcResult["hex"] != null)
			{
				uint256 blockHash = null;
				long? blockHeight = null;
				if (rpcResult["blockhash"] != null)
				{
					blockHash = uint256.Parse(rpcResult.Value<string>("blockhash"));
					var blockHeader = await client.GetBlockHeaderAsyncEx(blockHash);
					if (blockHeader is not null)
						blockHeight = blockHeader.Height;
					else
						blockHash = null;
				}
				DateTimeOffset timestamp = DateTimeOffset.UtcNow;
				if (rpcResult["time"] != null)
				{
					timestamp = NBitcoin.Utils.UnixTimeToDateTime(rpcResult.Value<long>("time"));
				}
				
				var rawTx = client.Network.Consensus.ConsensusFactory.CreateTransaction();
				rawTx.ReadWrite(Encoders.Hex.DecodeData(rpcResult.Value<string>("hex")), client.Network);
				return new SavedTransaction()
									{
										BlockHash = blockHash,
										BlockHeight = blockHeight,
										Timestamp = timestamp,
										Transaction = rawTx
									};
			}
			return null;
		}
	}
}
