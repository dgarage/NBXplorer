using NBXplorer.Logging;
using NBXplorer.ModelBinders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.RPC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using NBXplorer.Client;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json.Linq;

namespace NBXplorer.Controllers
{
	[Route("v1")]
	[Authorize]
	public class MainController : Controller
	{
		class AnnotatedTransaction
		{
			public int Height
			{
				get;
				internal set;
			}
			public TrackedTransaction Record
			{
				get;
				internal set;
			}
		}

		class AnnotatedTransactionCollection : List<AnnotatedTransaction>
		{
			public AnnotatedTransactionCollection(IEnumerable<AnnotatedTransaction> transactions) : base(transactions)
			{
				foreach(var tx in transactions)
				{
					_TxById.Add(tx.Record.Transaction.GetHash(), tx);
				}
			}

			MultiValueDictionary<uint256, AnnotatedTransaction> _TxById = new MultiValueDictionary<uint256, AnnotatedTransaction>();
			public IReadOnlyCollection<AnnotatedTransaction> GetByTxId(uint256 txId)
			{
				if(txId == null)
					throw new ArgumentNullException(nameof(txId));
				IReadOnlyCollection<AnnotatedTransaction> value;
				if(_TxById.TryGetValue(txId, out value))
					return value;
				return new List<AnnotatedTransaction>();
			}
		}

		public MainController(ExplorerRuntime runtime)
		{
			if(runtime == null)
				throw new ArgumentNullException("runtime");
			Runtime = runtime;
		}
		public ExplorerRuntime Runtime
		{
			get; set;
		}

		[HttpGet]
		[Route("fees/{blockCount}")]
		public async Task<GetFeeRateResult> GetFeeRate(int blockCount)
		{
			var result = await Runtime.RPC.SendCommandAsync("estimatesmartfee", blockCount);
			var feeRateProperty = ((JObject)result.Result).Property("feeRate");
			var rate = feeRateProperty == null ? (decimal)-1 : ((JObject)result.Result)["feerate"].Value<decimal>();
			if(rate == -1)
				throw new NBXplorerError(400, "fee-estimation-unavailable", $"It is currently impossible to estimate fees, please try again later.").AsException();
			return new GetFeeRateResult()
			{
				FeeRate = new FeeRate(Money.Coins(rate), 1000),
				BlockCount = ((JObject)result.Result)["blocks"].Value<int>()
			};
		}

		[HttpGet]
		[Route("addresses/{strategy}/unused")]
		public async Task<KeyPathInformation> GetUnusedAddress(
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase strategy, DerivationFeature feature = DerivationFeature.Deposit, int skip = 0, bool reserve = false)
		{
			if(strategy == null)
				throw new ArgumentNullException(nameof(strategy));
			try
			{
				var result = await Runtime.Repository.GetUnused(strategy, feature, skip, reserve);
				if(result == null)
					throw new NBXplorerError(404, "strategy-not-found", $"This strategy is not tracked, or you tried to skip too much unused addresses").AsException();
				return result;
			}
			catch(NotSupportedException)
			{
				throw new NBXplorerError(400, "derivation-not-supported", $"The derivation scheme {feature} is not supported").AsException();
			}
		}

		[HttpPost]
		[Route("addresses/{strategy}/cancelreservation")]
		public Task CancelReservation([ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase strategy, [FromBody]KeyPath[] keyPaths)
		{
			return Runtime.Repository.CancelReservation(strategy, keyPaths);
		}


		[HttpGet]
		[Route("ping")]
		public IActionResult Ping()
		{
			return Json("pong");
		}

		[HttpGet]
		[Route("tx/{txId}")]
		public IActionResult GetTransaction(
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 txId)
		{
			var result = Runtime.Repository.GetSavedTransactions(txId);
			if(result.Length == 0)
				return NotFound();

			var tx = result.First().Transaction;

			var confBlock = result
						.Where(r => r.BlockHash != null)
						.Select(r => Runtime.Chain.GetBlock(r.BlockHash))
						.Where(r => r != null)
						.FirstOrDefault();

			var conf = confBlock == null ? 0 : Runtime.Chain.Tip.Height - confBlock.Height + 1;

			return new FileContentResult(new TransactionResult() { Confirmations = conf, Transaction = tx }.ToBytes(), "application/octet-stream");
		}

		[HttpPost]
		[Route("track/{derivationStrategy}")]
		public Task TrackWallet(
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase derivationStrategy)
		{
			if(derivationStrategy == null)
				return Task.FromResult(NotFound());
			return Runtime.Repository.TrackAsync(derivationStrategy);
		}

		[HttpGet]
		[Route("sync/{extPubKey}")]
		public async Task<FileContentResult> Sync(
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase extPubKey,
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 confHash = null,
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 unconfHash = null,
			bool noWait = false)
		{
			if(extPubKey == null)
				throw new ArgumentNullException(nameof(extPubKey));

			var waitingTransaction = noWait ? Task.FromResult(false) : WaitingTransaction(extPubKey);
			UTXOChanges changes = null;
			var getKeyPaths = GetKeyPaths(extPubKey);
			var matchScript = MatchKeyPaths(getKeyPaths);

			while(true)
			{
				changes = new UTXOChanges();
				changes.CurrentHeight = Runtime.Chain.Height;
				var transactions = GetAnnotatedTransactions(extPubKey);

				var unconf = transactions.Where(tx => tx.Height == MempoolHeight);
				var conf = transactions.Where(tx => tx.Height != MempoolHeight);
				conf = conf.TopologicalSort(DependsOn(conf.ToList())).ToList();
				unconf = unconf.TopologicalSort(DependsOn(unconf.ToList())).ToList();


				var states = UTXOStateResult.CreateStates(matchScript, unconfHash, unconf.Select(c => c.Record.Transaction), confHash, conf.Select(c => c.Record.Transaction));

				var conflicted = states.Unconfirmed.Actual.Conflicts
					.SelectMany(c => c.Value)
					.SelectMany(txid => transactions.GetByTxId(txid))
					.Where(a => a.Height == MempoolHeight)
					.Select(a => a.Record)
					.Distinct()
					.ToList();

				if(conflicted.Count != 0)
				{
					Logs.Explorer.LogInformation($"Clean {conflicted.Count} conflicted transactions");
					Runtime.Repository.CleanTransactions(extPubKey, conflicted);
				}

				changes.Confirmed = SetUTXOChange(states.Confirmed);
				changes.Unconfirmed = SetUTXOChange(states.Unconfirmed, states.Confirmed.Actual);



				FillUTXOsInformation(changes.Confirmed.UTXOs, getKeyPaths, transactions, changes.CurrentHeight);
				FillUTXOsInformation(changes.Unconfirmed.UTXOs, getKeyPaths, transactions, changes.CurrentHeight);

				if(changes.HasChanges || !(await waitingTransaction))
					break;
				waitingTransaction = Task.FromResult(false); //next time, will not wait
			}

			return new FileContentResult(changes.ToBytes(), "application/octet-stream");
		}

		private void FillUTXOsInformation(List<UTXO> utxos, Func<Script[], KeyPath[]> getKeyPaths, AnnotatedTransactionCollection transactionsById, int currentHeight)
		{
			var keyPaths = getKeyPaths(utxos.Select(u => u.Output.ScriptPubKey).ToArray());
			for(int i = 0; i < utxos.Count; i++)
			{
				var utxo = utxos[i];
				utxo.KeyPath = keyPaths[i];
				var txHeight = transactionsById.GetByTxId(utxo.Outpoint.Hash).Select(r => r.Height).Min();
				txHeight = txHeight == MempoolHeight ? currentHeight + 1 : txHeight;
				utxo.Confirmations = currentHeight - txHeight + 1;
			}
		}

		private UTXOChange SetUTXOChange(UTXOStates states, UTXOState substract = null)
		{
			substract = substract ?? new UTXOState();
			var substractedSpent = new HashSet<OutPoint>(substract.SpentOutpoints);
			var substractedReceived = new HashSet<OutPoint>(substract.CoinsByOutpoint.Select(u => u.Key));

			UTXOChange change = new UTXOChange();
			change.Reset = states.Known == null;
			change.Hash = states.Actual.CurrentHash;

			states.Known = states.Known ?? new UTXOState();

			foreach(var coin in states.Actual.CoinsByOutpoint)
			{
				if(!states.Known.CoinsByOutpoint.ContainsKey(coin.Key) &&
					!substractedReceived.Contains(coin.Key))
					change.UTXOs.Add(new UTXO() { Outpoint = coin.Key, Output = coin.Value.TxOut });
			}

			foreach(var outpoint in states.Actual.SpentOutpoints)
			{
				if(!states.Known.SpentOutpoints.Contains(outpoint) &&
					states.Known.CoinsByOutpoint.ContainsKey(outpoint) &&
					!substractedSpent.Contains(outpoint))
					change.SpentOutpoints.Add(outpoint);
			}
			return change;
		}

		private AnnotatedTransactionCollection GetAnnotatedTransactions(DerivationStrategyBase extPubKey)
		{
			return new AnnotatedTransactionCollection(Runtime.Repository
									.GetTransactions(extPubKey)
									.Select(t =>
									new AnnotatedTransaction
									{
										Height = GetHeight(t.BlockHash),
										Record = t
									})
									.Where(u => u.Height != OrphanHeight));
		}

		Func<AnnotatedTransaction, IEnumerable<AnnotatedTransaction>> DependsOn(IEnumerable<AnnotatedTransaction> transactions)
		{
			return t =>
			{
				HashSet<uint256> dependsOn = new HashSet<uint256>(t.Record.Transaction.Inputs.Select(txin => txin.PrevOut.Hash));
				return transactions.Where(u => dependsOn.Contains(u.Record.Transaction.GetHash()) ||  //Depends on parent transaction
												((u.Height < t.Height))); //Depends on earlier transaction
			};
		}

		private async Task<bool> WaitingTransaction(DerivationStrategyBase extPubKey)
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			cts.CancelAfter(10000);

			try
			{
				if(!await Runtime.WaitFor(extPubKey, cts.Token))
				{
					return false;
				}
			}
			catch(OperationCanceledException) { return false; }
			return true;
		}


		private Func<Script[], bool[]> MatchKeyPaths(Func<Script[], KeyPath[]> getKeyPaths)
		{
			return (scripts) => getKeyPaths(scripts).Select(c => c != null).ToArray();
		}
		private Func<Script[], KeyPath[]> GetKeyPaths(DerivationStrategyBase extPubKey)
		{
			Dictionary<Script, KeyPath> cache = new Dictionary<Script, KeyPath>();
			return (scripts) =>
			{
				KeyPath[] result = new KeyPath[scripts.Length];
				for(int i = 0; i < result.Length; i++)
				{
					if(cache.TryGetValue(scripts[i], out KeyPath keypath))
						result[i] = keypath;
				}

				var needFetch = scripts.Where((r, i) => result[i] == null).ToArray();
				var fetched = Runtime.Repository.GetKeyInformations(needFetch).GetAwaiter().GetResult();
				for(int i = 0; i < fetched.Length; i++)
				{
					var keyInfos = fetched[i];
					var script = needFetch[i];
					foreach(var keyInfo in keyInfos)
					{
						if(keyInfo.DerivationStrategy == extPubKey)
						{
							cache.TryAdd(script, keyInfo.KeyPath);
							break;
						}
					}
				}

				for(int i = 0; i < result.Length; i++)
				{
					if(cache.TryGetValue(scripts[i], out KeyPath keypath))
						result[i] = keypath;
				}

				return result;
			};
		}

		const int MempoolHeight = int.MaxValue;
		const int OrphanHeight = int.MaxValue - 1;
		private int GetHeight(uint256 blockHash)
		{
			if(blockHash == null)
				return MempoolHeight;
			var header = Runtime.Chain.GetBlock(blockHash);
			return header == null ? OrphanHeight : header.Height;
		}

		[HttpPost]
		[Route("broadcast")]
		public async Task<BroadcastResult> Broadcast(
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase extPubKey)
		{
			var tx = new Transaction();
			var stream = new BitcoinStream(Request.Body, false);
			tx.ReadWrite(stream);
			RPCException rpcEx = null;
			try
			{
				await Runtime.RPC.SendRawTransactionAsync(tx);
				return new BroadcastResult(true);
			}
			catch(RPCException ex)
			{
				rpcEx = ex;
				Logs.Explorer.LogInformation($"Transaction {tx.GetHash()} failed to broadcast (Code: {ex.RPCCode}, Message: {ex.RPCCodeMessage}, Details: {ex.Message} )");
				if(extPubKey != null && ex.Message.StartsWith("Missing inputs", StringComparison.OrdinalIgnoreCase))
				{
					Logs.Explorer.LogInformation("Trying to broadcast unconfirmed of the wallet");
					var transactions = GetAnnotatedTransactions(extPubKey).Where(t => t.Height == MempoolHeight).ToList();
					transactions = transactions.TopologicalSort(DependsOn(transactions)).ToList();
					foreach(var existing in transactions)
					{
						try
						{
							await Runtime.RPC.SendRawTransactionAsync(existing.Record.Transaction);
						}
						catch { }
					}

					try
					{

						await Runtime.RPC.SendRawTransactionAsync(tx);
						Logs.Explorer.LogInformation($"Broadcast success");
						return new BroadcastResult(true);
					}
					catch(RPCException)
					{
						Logs.Explorer.LogInformation($"Transaction {tx.GetHash()} failed to broadcast (Code: {ex.RPCCode}, Message: {ex.RPCCodeMessage}, Details: {ex.Message} )");
					}
				}
				return new BroadcastResult(false)
				{
					RPCCode = rpcEx.RPCCode,
					RPCCodeMessage = rpcEx.RPCCodeMessage,
					RPCMessage = rpcEx.Message
				};
			}
		}
	}
}
