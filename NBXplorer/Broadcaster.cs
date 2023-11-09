using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.RPC;
using NBXplorer.Backends;
using System;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class BroadcasterResult
	{
		public bool Rebroadcasted { get; set; }
		public bool AlreadyInMempool { get; set; }
		public bool UnknownError { get; set; }
		public bool MissingInput { get; set; }
		public bool MempoolConflict { get; set; }
		public bool NotEnoughFee { get; set; }
	}
	public class Broadcaster
	{
		public Broadcaster(IIndexers indexers, ILoggerFactory loggerFactory)
		{
			Indexers = indexers;
			LoggerFactory = loggerFactory;
		}

		public IIndexers Indexers { get; }
		public ILoggerFactory LoggerFactory { get; }
		record Reject
		{
			public record MempoolConflict : Reject;
			public record MissingInput : Reject;
			public record InMempoolAlready : Reject;
			public record NotEnoughFee : Reject;
			public record Unknown(String RejectReason) : Reject;
		}

		Reject GetRejectReason(string rejectReason) =>
		rejectReason switch
		{
			"txn-already-in-mempool" => new Reject.InMempoolAlready(),
			"insufficient fee" => new Reject.MempoolConflict(),
			"txn-mempool-conflict" => new Reject.MempoolConflict(),
			{ } s when s.Contains("rejecting replacement", StringComparison.OrdinalIgnoreCase) => new Reject.MempoolConflict(),
			"Missing inputs" or
			"bad-txns-inputs-spent" or
			"bad-txns-inputs-missingorspent" or
			"txn-already-known" or
			"missing-inputs" or
			"Transaction already in block chain" => new Reject.MissingInput(),
			"mempool min fee not met" => new Reject.NotEnoughFee(),
			_ => new Reject.Unknown(rejectReason)
		};

		public async Task<BroadcasterResult> Broadcast(NBXplorerNetwork network, Transaction tx, uint256 transactionId = null)
		{
			transactionId ??= tx.GetHash();
			BroadcasterResult result = new BroadcasterResult();
			var indexer = Indexers.GetIndexer(network);
			var logger = LoggerFactory.CreateLogger($"NBXplorer.Broadcaster.{network.CryptoCode}");
			var rpc = indexer.GetConnectedClient();
			if (rpc is null)
				return result;
			bool broadcast = true;
			try
			{
				try
				{
					var accepted = await rpc.TestMempoolAcceptAsync(tx);
					if (!accepted.IsAllowed)
					{
						var rejectReason = GetRejectReason(accepted.RejectReason);
						SetResult(rejectReason, result);
						broadcast = rejectReason is Reject.Unknown;
					}
				}
				catch (RPCException ex) when (ex.RPCCode == RPCErrorCode.RPC_METHOD_NOT_FOUND)
				{
				}

				if (broadcast)
				{
					await rpc.SendRawTransactionAsync(tx);
					result.Rebroadcasted = true;
					logger.LogInformation($"Rebroadcasted {transactionId}");
				}
			}
			catch (RPCException ex)
			{
				var rejectReason = GetRejectReason(ex.Message);
				if (ex.RPCCode == RPCErrorCode.RPC_TRANSACTION_ALREADY_IN_CHAIN)
					rejectReason = new Reject.MissingInput();
				SetResult(rejectReason, result);
				if (rejectReason is Reject.Unknown u)
					logger.LogInformation($"Unknown exception when broadcasting {tx.GetHash()} ({u.RejectReason})");
			}
			return result;
		}

		private void SetResult(Reject rejectReason, BroadcasterResult result)
		{
			if (rejectReason is Reject.InMempoolAlready)
			{
				result.AlreadyInMempool = true;
			}
			else if (rejectReason is Reject.MissingInput)
			{
				result.MissingInput = true;
			}
			else if (rejectReason is Reject.MempoolConflict)
			{
				result.MissingInput = true;
				result.MempoolConflict = true;
			}
			else if (rejectReason is Reject.NotEnoughFee)
			{
				result.NotEnoughFee = true;
			}
			else if (rejectReason is Reject.Unknown)
			{
				result.UnknownError = true;
			}
		}
	}
}
