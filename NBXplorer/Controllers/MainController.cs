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
		[Route("addresses/{strategy}/unused")]
		public KeyPathInformation GetUnusedAddress(
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			IDerivationStrategy strategy, DerivationFeature feature = DerivationFeature.Deposit, int skip = 0, bool reserve = false)
		{
			if(strategy == null)
				throw new ArgumentNullException(nameof(strategy));
			try
			{
				var result = Runtime.Repository.GetUnused(strategy, feature, skip, reserve);
				if(result == null)
					throw new NBXplorerError(404, "strategy-not-found", $"This strategy is not tracked, or you tried to skip too much unused addresses").AsException();
				return result;
			}
			catch(NotSupportedException)
			{
				throw new NBXplorerError(400, "derivation-not-supported", $"The derivation scheme {feature} is not supported").AsException();
			}
		}

		[HttpGet]
		[Route("ping")]
		public string Ping()
		{
			return "pong";
		}

		[HttpGet]
		[Route("sync/{extPubKey}")]
		public async Task<FileContentResult> Sync(
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			IDerivationStrategy extPubKey,
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 confHash = null,
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 unconfHash = null,
			bool noWait = false)
		{
			if(extPubKey == null)
				throw new ArgumentNullException(nameof(extPubKey));

			var waitingTransaction = noWait ? Task.FromResult(false) : WaitingTransaction(extPubKey);

			Runtime.Repository.MarkAsUsed(new KeyInformation(extPubKey));
			UTXOChanges changes = null;
			var getKeyPath = GetKeyPaths(extPubKey);
			UTXOState utxoState = null;

			while(true)
			{
				utxoState = new UTXOState();
				utxoState.MatchScript = (script) => getKeyPath(script) != null;
				changes = new UTXOChanges();
				changes.CurrentHeight = Runtime.Chain.Height;
				var transactions = GetAnnotatedTransactions(extPubKey);
				var unconf = transactions.Where(tx => tx.Height == MempoolHeight);
				var conf = transactions.Where(tx => tx.Height != MempoolHeight);

				conf = conf.TopologicalSort(DependsOn(conf.ToList())).ToList();
				unconf = unconf.TopologicalSort(DependsOn(unconf.ToList())).ToList();


				var transactionsById = new Dictionary<uint256, AnnotatedTransaction>();
				var unconfById = new Dictionary<uint256, AnnotatedTransaction>();

				UTXOState knownConf = confHash == uint256.Zero ? new UTXOState() : null;

				foreach(var item in conf)
				{
					transactionsById.TryAdd(item.Record.Transaction.GetHash(), item);

					var applyResult = utxoState.Apply(item.Record.Transaction);
					if(applyResult == ApplyTransactionResult.Conflict)
					{
						Logs.Explorer.LogError("A conflict among confirmed transaction happened, this should be impossible");
						throw new InvalidOperationException("The impossible happened");
					}

					if(applyResult == ApplyTransactionResult.Passed)
					{
						if(utxoState.CurrentHash == confHash)
							knownConf = utxoState.Snapshot();
					}
				}

				var actualConf = utxoState.Snapshot();
				utxoState.ResetEvents();

				UTXOState knownUnconf = null;
				foreach(var item in unconf)
				{
					var txid = item.Record.Transaction.GetHash();
					transactionsById.TryAdd(txid, item);
					unconfById.TryAdd(txid, item);
					if(utxoState.Apply(item.Record.Transaction) == ApplyTransactionResult.Passed)
					{
						if(utxoState.CurrentHash == unconfHash)
							knownUnconf = utxoState.Snapshot();
					}
				}
				knownUnconf = knownUnconf ?? actualConf;

				var actualUnconf = utxoState.Snapshot();
				actualUnconf.Remove(actualConf);

				var conflicted = utxoState.Conflicts
					.SelectMany(c => c.Value)
					.Select(txid => unconfById[txid].Record)
					.ToList();

				if(conflicted.Count != 0)
				{
					Logs.Explorer.LogInformation($"Clean {conflicted.Count} conflicted transactions");
					Runtime.Repository.CleanTransactions(extPubKey, conflicted);
				}

				changes.Unconfirmed = SetUTXOChange(knownUnconf, actualUnconf);
				changes.Unconfirmed.Reset = knownUnconf == actualConf && unconfHash != uint256.Zero;
				changes.Confirmed = SetUTXOChange(knownConf, actualConf);

				FillUTXOsInformation(changes.Confirmed.UTXOs, getKeyPath, transactionsById, changes.CurrentHeight);
				FillUTXOsInformation(changes.Unconfirmed.UTXOs, getKeyPath, transactionsById, changes.CurrentHeight);

				if(changes.HasChanges || !(await waitingTransaction))
					break;
				waitingTransaction = Task.FromResult(false); //next time, will not wait
			}

			return new FileContentResult(changes.ToBytes(), "application/octet-stream");

		}

		private void FillUTXOsInformation(List<UTXO> utxos, Func<Script, KeyPath> getKeyPath, Dictionary<uint256, AnnotatedTransaction> transactionsById, int currentHeight)
		{
			foreach(var utxo in utxos)
			{
				utxo.KeyPath = getKeyPath(utxo.Output.ScriptPubKey);
				var txHeight = transactionsById[utxo.Outpoint.Hash].Height;
				txHeight = txHeight == MempoolHeight ? currentHeight + 1 : txHeight;
				utxo.Confirmations = currentHeight - txHeight + 1;
			}
		}

		private UTXOChange SetUTXOChange(UTXOState known, UTXOState actual)
		{
			UTXOChange change = new UTXOChange();
			change.Reset = known == null;
			change.Hash = actual.CurrentHash;

			known = known ?? new UTXOState();

			foreach(var coin in actual.CoinsByOutpoint)
			{
				if(!known.CoinsByOutpoint.ContainsKey(coin.Key))
					change.UTXOs.Add(new UTXO() { Outpoint = coin.Key, Output = coin.Value.TxOut });
			}

			foreach(var outpoint in actual.SpentOutpoints)
			{
				if(!known.SpentOutpoints.Contains(outpoint) && known.CoinsByOutpoint.ContainsKey(outpoint))
					change.SpentOutpoints.Add(outpoint);
			}
			return change;
		}

		private List<AnnotatedTransaction> GetAnnotatedTransactions(IDerivationStrategy extPubKey)
		{
			return Runtime.Repository
									.GetTransactions(extPubKey)
									.Select(t =>
									new AnnotatedTransaction
									{
										Height = GetHeight(t.BlockHash),
										Record = t
									})
									.Where(u => u.Height != OrphanHeight)
									.ToList();
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

		private async Task<bool> WaitingTransaction(IDerivationStrategy extPubKey)
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

		private Func<Script, KeyPath> GetKeyPaths(IDerivationStrategy extPubKey)
		{
			Dictionary<Script, KeyPath> cache = new Dictionary<Script, KeyPath>();
			return (script) =>
			{
				KeyPath result;
				if(cache.TryGetValue(script, out result))
					return result;
				result = Runtime.Repository.GetKeyInformation(extPubKey, script)?.KeyPath;
				if(result != null)
					cache.TryAdd(script, result);
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
			IDerivationStrategy extPubKey)
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
