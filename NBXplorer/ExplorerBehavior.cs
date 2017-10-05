using NBXplorer.Logging;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Behaviors;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using NBitcoin.Crypto;
using Completion = System.Threading.Tasks.TaskCompletionSource<bool>;
using NBXplorer.DerivationStrategy;

namespace NBXplorer
{
	public class ExplorerBehavior : NodeBehavior
	{
		public ExplorerBehavior(ExplorerRuntime runtime, ConcurrentChain chain)
		{
			if(runtime == null)
				throw new ArgumentNullException("runtime");
			if(chain == null)
				throw new ArgumentNullException(nameof(chain));
			_Chain = chain;
			_Runtime = runtime;
		}


		private readonly ConcurrentChain _Chain;
		public ConcurrentChain Chain
		{
			get
			{
				return _Chain;
			}
		}

		private readonly ExplorerRuntime _Runtime;
		public ExplorerRuntime Runtime
		{
			get
			{
				return _Runtime;
			}
		}

		public int StartHeight
		{
			get;
			set;
		}

		public override object Clone()
		{
			return new ExplorerBehavior(Runtime, _Chain) { StartHeight = StartHeight };
		}

		Timer _Timer;

		protected override void AttachCore()
		{
			AttachedNode.StateChanged += AttachedNode_StateChanged;
			AttachedNode.MessageReceived += AttachedNode_MessageReceived;
			_CurrentLocation = Runtime.Repository.GetIndexProgress() ?? GetDefaultCurrentLocation();
			Logs.Explorer.LogInformation("Starting scan at block " + Chain.FindFork(_CurrentLocation).Height);
			_Timer = new Timer(Tick, null, 0, 30);
		}

		private BlockLocator GetDefaultCurrentLocation()
		{
			if(StartHeight > Chain.Height)
				throw new InvalidOperationException("StartHeight should not be above the current tip");
			return StartHeight == -1 ?
				new BlockLocator() { Blocks = new List<uint256>() { Chain.Tip.HashBlock } } :
				new BlockLocator() { Blocks = new List<uint256>() { Chain.GetBlock(StartHeight).HashBlock } };
		}

		public async Task WaitFor(DerivationStrategyBase pubKey, CancellationToken cancellation = default(CancellationToken))
		{
			TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();

			var key = pubKey.GetHash();

			lock(_WaitFor)
			{
				_WaitFor.Add(key, completion);
			}

			cancellation.Register(() =>
			{
				completion.TrySetCanceled();
			});

			try
			{
				await completion.Task;
			}
			finally
			{
				lock(_WaitFor)
				{
					_WaitFor.Remove(key, completion);
				}
			}
		}

		MultiValueDictionary<uint160, Completion> _WaitFor = new MultiValueDictionary<uint160, Completion>();

		public void AskBlocks()
		{
			var node = AttachedNode;
			if(node == null || node.State != NodeState.HandShaked)
				return;
			var pendingTip = node.Behaviors.Find<ChainBehavior>().PendingTip;
			if(pendingTip == null || pendingTip.Height < node.PeerVersion.StartHeight)
				return;
			if(_InFlights.Count != 0)
				return;
			var currentLocation = _CurrentLocation ?? GetDefaultCurrentLocation();
			var currentBlock = Chain.FindFork(currentLocation);
			if(currentBlock.Height < StartHeight)
				currentBlock = Chain.GetBlock(StartHeight) ?? pendingTip;

			//Up to date
			if(pendingTip.HashBlock == currentBlock.HashBlock)
				return;

			var toDownload = pendingTip.EnumerateToGenesis().TakeWhile(b => b.HashBlock != currentBlock.HashBlock).ToArray();
			Array.Reverse(toDownload);
			var invs = toDownload.Take(10)
				.Select(b => new InventoryVector(node.AddSupportedOptions(InventoryType.MSG_BLOCK), b.HashBlock))
				.Where(b => _InFlights.TryAdd(b.Hash, new Download()))
				.ToArray();

			if(invs.Length != 0)
			{
				node.SendMessageAsync(new GetDataPayload(invs));
			}
		}

		class Download
		{
		}

		ConcurrentDictionary<uint256, Download> _InFlights = new ConcurrentDictionary<uint256, Download>();


		void Tick(object state)
		{
			try
			{
				AskBlocks();
			}
			catch(Exception ex)
			{
				if(AttachedNode == null)
					return;
				Logs.Explorer.LogError("Exception in ExplorerBehavior tick loop");
				Logs.Explorer.LogError(ex.ToString());
			}
		}



		BlockLocator _CurrentLocation;

		protected override void DetachCore()
		{
			AttachedNode.StateChanged -= AttachedNode_StateChanged;
			AttachedNode.MessageReceived -= AttachedNode_MessageReceived;

			_Timer.Dispose();
			_Timer = null;
		}

		private void AttachedNode_MessageReceived(Node node, IncomingMessage message)
		{
			message.Message.IfPayloadIs<InvPayload>(invs =>
			{
				var data = new GetDataPayload();
				foreach(var inv in invs.Inventory)
				{
					inv.Type = node.AddSupportedOptions(inv.Type);
					if(inv.Type.HasFlag(InventoryType.MSG_TX))
						data.Inventory.Add(inv);
				}
				if(data.Inventory.Count != 0)
					node.SendMessageAsync(data);
			});

			message.Message.IfPayloadIs<HeadersPayload>(headers =>
			{
				if(headers.Headers.Count == 0)
					return;
				AskBlocks();
			});

			message.Message.IfPayloadIs<BlockPayload>(block =>
			{
				block.Object.Header.CacheHashes();
				Download o;
				if(_InFlights.ContainsKey(block.Object.GetHash()))
				{
					var blockHeader = Runtime.Chain.GetBlock(block.Object.GetHash());
					if(blockHeader == null)
						return;
					var currentLocation = blockHeader.GetLocator();
					_CurrentLocation = currentLocation;
					if(_InFlights.TryRemove(block.Object.GetHash(), out o))
					{
						var pubKeys = new HashSet<DerivationStrategyBase>();
						foreach(var tx in block.Object.Transactions)
							tx.CacheHashes();


						List<InsertTransaction> trackedTransactions = new List<InsertTransaction>();
						foreach(var tx in block.Object.Transactions)
						{
							var pubKeys2 = GetInterestedWallets(tx);
							foreach(var pubkey in pubKeys2)
							{
								pubKeys.Add(pubkey);
								trackedTransactions.Add(
									new InsertTransaction()
									{
										PubKey = pubkey,
										TrackedTransaction = new TrackedTransaction()
										{
											BlockHash = block.Object.GetHash(),
											Transaction = tx
										}
									});
							}
						}
						Runtime.Repository.InsertTransactions(trackedTransactions.ToArray());
						Runtime.Repository.SaveTransactions(trackedTransactions.Select(t => t.TrackedTransaction.Transaction).ToArray(), block.Object.GetHash());

						//Save index progress everytimes if not synching, or once every 100 blocks otherwise
						if(!IsSynching() || block.Object.GetHash().GetLow32() % 100 == 0)
							Runtime.Repository.SetIndexProgress(currentLocation);
						Logs.Explorer.LogInformation($"Processed block {block.Object.GetHash()}");

						foreach(var pubkey in pubKeys)
						{
							Notify(pubkey, false);
						}
					}
					if(_InFlights.Count == 0)
						AskBlocks();
				}
			});

			message.Message.IfPayloadIs<TxPayload>(txPayload =>
			{
				var pubKeys = GetInterestedWallets(txPayload.Object);
				var insertedTransactions = new List<InsertTransaction>();
				foreach(var pubkey in pubKeys)
				{
					insertedTransactions.Add(
						new InsertTransaction()
						{
							PubKey = pubkey,
							TrackedTransaction = new TrackedTransaction()
							{
								Transaction = txPayload.Object
							}
						});
				}
				Runtime.Repository.InsertTransactions(insertedTransactions.ToArray());
				Runtime.Repository.SaveTransactions(insertedTransactions.Select(t => t.TrackedTransaction.Transaction).ToArray(), null);

				foreach(var pubkey in pubKeys)
				{
					Notify(pubkey, true);
				}
			});

		}

		private bool IsSynching()
		{
			var location = _CurrentLocation;
			if(location == null)
				return true;
			var fork = Chain.FindFork(location);
			return Chain.Tip.Height - fork.Height > 10;
		}

		private void Notify(DerivationStrategyBase pubkey, bool log)
		{
			if(log)
				Logs.Explorer.LogInformation($"A wallet received money");
			var key = pubkey.GetHash();
			lock(_WaitFor)
			{
				IReadOnlyCollection<Completion> completions;
				if(_WaitFor.TryGetValue(key, out completions))
				{
					foreach(var completion in completions.ToList())
					{
						completion.TrySetResult(true);
					}
				}
			}
		}

		private HashSet<DerivationStrategyBase> GetInterestedWallets(Transaction tx)
		{
			var pubKeys = new HashSet<DerivationStrategyBase>();
			tx.CacheHashes();
			foreach(var input in tx.Inputs)
			{
				var signer = input.ScriptSig.GetSigner() ?? input.WitScript.ToScript().GetSigner();
				if(signer != null)
				{
					foreach(var keyInfo in Runtime.Repository.GetKeyInformations(signer.ScriptPubKey).GetAwaiter().GetResult())
					{
						pubKeys.Add(keyInfo.DerivationStrategy);
						Runtime.Repository.MarkAsUsedAsync(keyInfo).GetAwaiter().GetResult();
					}
				}
			}

			foreach(var output in tx.Outputs)
			{
				foreach(var keyInfo in Runtime.Repository.GetKeyInformations(output.ScriptPubKey).GetAwaiter().GetResult())
				{
					pubKeys.Add(keyInfo.DerivationStrategy);
					Runtime.Repository.MarkAsUsedAsync(keyInfo).GetAwaiter().GetResult();
				}
			}
			return pubKeys;
		}

		private void AttachedNode_StateChanged(Node node, NodeState oldState)
		{
			if(node.State == NodeState.HandShaked)
			{
				Logs.Explorer.LogInformation($"Handshaked Bitcoin node");
				node.SendMessageAsync(new SendHeadersPayload());
				node.SendMessageAsync(new MempoolPayload());
				AskBlocks();
			}
			if(node.State == NodeState.Offline)
				Logs.Explorer.LogInformation($"Closed connection with Bitcoin node");
			if(node.State == NodeState.Failed)
				Logs.Explorer.LogError($"Connection with Bitcoin unexpectedly failed: {node.DisconnectReason.Reason}");
		}
	}
}
