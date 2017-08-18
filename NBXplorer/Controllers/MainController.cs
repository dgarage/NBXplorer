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
using NBXplorer.Client.Models;

namespace NBXplorer.Controllers
{
	[Route("v1")]
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
		[Route("{strategy}/unused")]
		public KeyPathInformation GetUnusedAddress(
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			IDerivationStrategy strategy, DerivationFeature feature = DerivationFeature.Deposit, int skip = 0)
		{
			if(strategy == null)
				throw new ArgumentNullException(nameof(strategy));
			try
			{
				var result = Runtime.Repository.GetUnused(strategy, feature, skip);
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
			confHash = confHash ?? uint256.Zero;
			var actualLastBlockHash = uint256.Zero;

			var waitingTransaction = noWait ? Task.FromResult(false) : WaitingTransaction(extPubKey);

			Runtime.Repository.MarkAsUsed(new KeyInformation(extPubKey));
			UTXOChanges changes = null;
			UTXOChanges previousChanges = null;
			List<TrackedTransaction> cleanList = null;
			var getKeyPath = GetKeyPaths(extPubKey);

			while(true)
			{
				cleanList = new List<TrackedTransaction>();
				HashSet<uint256> conflictedUnconf = new HashSet<uint256>();
				changes = new UTXOChanges();
				changes.CurrentHeight = Runtime.Chain.Height;
				List<AnnotatedTransaction> transactions = GetAnnotatedTransactions(extPubKey);
				var unconf = transactions.Where(tx => tx.Height == MempoolHeight);
				var conf = transactions.Where(tx => tx.Height != MempoolHeight);

				conf = conf.TopologicalSort(DependsOn(conf.ToList())).ToList();
				unconf = unconf.TopologicalSort(DependsOn(unconf.ToList())).ToList();
				foreach(var item in conf.Concat(unconf))
				{
					var record = item.Record;
					if(record.BlockHash == null)
					{
						if( //A parent conflicted with the current utxo
							record.Transaction.Inputs.Any(i => conflictedUnconf.Contains(i.PrevOut.Hash))
							||
							//Conflict with the confirmed utxo
							changes.Confirmed.HasConflict(record.Transaction))
						{
							cleanList.Add(record);
							conflictedUnconf.Add(record.Transaction.GetHash());
							continue;
						}
						if(changes.Unconfirmed.HasConflict(record.Transaction))
						{
							Logs.Explorer.LogInformation($"Conflicts in the mempool. {record.Transaction.GetHash()} ignored");
							continue;
						}
						changes.Unconfirmed.LoadChanges(record.Transaction, 0, getKeyPath);
					}
					else
					{
						if(changes.Confirmed.HasConflict(record.Transaction))
						{
							Logs.Explorer.LogError("A conflict among confirmed transaction happened, this should be impossible");
							throw new InvalidOperationException("The impossible happened");
						}
						changes.Unconfirmed.LoadChanges(record.Transaction, 0, getKeyPath);
						changes.Confirmed.LoadChanges(record.Transaction, Math.Max(0, changes.CurrentHeight - item.Height + 1), getKeyPath);
						changes.Confirmed.Hash = record.BlockHash;
						actualLastBlockHash = record.BlockHash;
						if(record.BlockHash == confHash)
							previousChanges = changes.Clone();
					}
				}

				changes.Unconfirmed = changes.Unconfirmed.Diff(changes.Confirmed);
				changes.Unconfirmed.Hash = changes.Unconfirmed.GetHash();
				if(changes.Unconfirmed.Hash == unconfHash)
					changes.Unconfirmed.Clear();
				else
					changes.Unconfirmed.Reset = true;


				if(actualLastBlockHash == confHash)
					changes.Confirmed.Clear();
				else if(previousChanges != null)
				{
					changes.Confirmed.Reset = false;
					changes.Confirmed = changes.Confirmed.Diff(previousChanges.Confirmed);
				}
				else
				{
					changes.Confirmed.Reset = true;
					changes.Confirmed.SpentOutpoints.Clear();
				}

				if(changes.HasChanges || !(await waitingTransaction))
					break;
				waitingTransaction = Task.FromResult(false); //next time, will not wait
			}

			Runtime.Repository.CleanTransactions(extPubKey, cleanList);

			return new FileContentResult(changes.ToBytes(), "application/octet-stream");

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
			return (script) =>
			{
				return Runtime.Repository.GetKeyInformation(extPubKey, script)?.KeyPath;
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
