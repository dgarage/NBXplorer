using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.RPC;
using NBXplorer.Backends;
using System;
using System.Linq;
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
		static string[] missingInputCodes = new string[]
		{
			"Missing inputs",
			"bad-txns-inputs-spent",
			"bad-txns-inputs-missingorspent",
			"txn-mempool-conflict",
			"missing-inputs",
			"txn-already-known",
			"Transaction already in block chain"
		};
		private bool IsMissingInput(string rejectReason) =>
		 missingInputCodes.Contains(rejectReason, StringComparer.OrdinalIgnoreCase) ||
		IsMempoolConflict(rejectReason);
		private bool IsMempoolConflict(string rejectReason) =>
			rejectReason.Equals("txn-mempool-conflict", StringComparison.OrdinalIgnoreCase) ||
			rejectReason.StartsWith("rejecting replacement", StringComparison.OrdinalIgnoreCase);

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
					if (accepted.IsAllowed)
						broadcast = true;
					else if (accepted.RejectReason == "txn-already-in-mempool")
					{
						result.AlreadyInMempool = true;
						broadcast = false;
					}
					else if (IsMissingInput(accepted.RejectReason))
					{
						broadcast = false;
						result.MissingInput = true;
						if (IsMempoolConflict(accepted.RejectReason))
							result.MempoolConflict = true;
					}
					else if (accepted.RejectReason == "mempool min fee not met")
					{
						broadcast = false;
						result.NotEnoughFee = true;
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
			catch (RPCException ex) when (ex.Message == "txn-already-in-mempool")
			{
				result.AlreadyInMempool = true;
			}
			catch (RPCException ex) when (
			ex.RPCCode == RPCErrorCode.RPC_TRANSACTION_ALREADY_IN_CHAIN ||
			IsMissingInput(ex.Message))
			{
				result.MissingInput = true;
				if (IsMempoolConflict(ex.Message))
					result.MempoolConflict = true;
			}
			catch (Exception ex)
			{
				result.UnknownError = true;
				logger.LogInformation($"Unknown exception when broadcasting {tx.GetHash()} ({ex.Message})");
			}
			return result;
		}
	}
}
