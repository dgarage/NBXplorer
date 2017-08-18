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
		class DerivationStrategyWrapper
		{
			IDerivationStrategy _Strat;
			public DerivationStrategyWrapper(IDerivationStrategy strat)
			{
				_Strat = strat;
			}

			public IDerivationStrategy Strat
			{
				get
				{
					return _Strat;
				}
			}

			public override bool Equals(object obj)
			{
				DerivationStrategyWrapper item = obj as DerivationStrategyWrapper;
				if(item == null)
					return false;
				return _Strat.GetHash().Equals(item._Strat.GetHash());
			}
			public static bool operator ==(DerivationStrategyWrapper a, DerivationStrategyWrapper b)
			{
				if(System.Object.ReferenceEquals(a, b))
					return true;
				if(((object)a == null) || ((object)b == null))
					return false;
				return a._Strat.GetHash() == b._Strat.GetHash();
			}

			public static bool operator !=(DerivationStrategyWrapper a, DerivationStrategyWrapper b)
			{
				return !(a == b);
			}

			public override int GetHashCode()
			{
				return _Strat.GetHash().GetHashCode();
			}
		}

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
			return new ExplorerBehavior(Runtime, _Chain);
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

		public async Task WaitFor(IDerivationStrategy pubKey, CancellationToken cancellation = default(CancellationToken))
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
						var pubKeys = new HashSet<DerivationStrategyWrapper>();
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
										PubKey = pubkey.Strat,
										TrackedTransaction = new TrackedTransaction()
										{
											BlockHash = block.Object.GetHash(),
											Transaction = tx
										}
									});
							}
						}
						Runtime.Repository.InsertTransactions(trackedTransactions.ToArray());

						//Save index progress everytimes if not synching, or once every 100 blocks otherwise
						if(!IsSynching() || block.Object.GetHash().GetLow32() % 100 == 0)
							Runtime.Repository.SetIndexProgress(currentLocation);
						Logs.Explorer.LogInformation($"Processed block {block.Object.GetHash()}");

						foreach(var pubkey in pubKeys)
						{
							Notify(pubkey.Strat, false);
						}
					}
					if(_InFlights.Count == 0)
						AskBlocks();
				}
			});

			message.Message.IfPayloadIs<TxPayload>(txPayload =>
			{
				var pubKeys = GetInterestedWallets(txPayload.Object);

				foreach(var pubkey in pubKeys)
				{
					Runtime.Repository.InsertTransactions(new[]
					{
						new InsertTransaction()
						{
							PubKey = pubkey.Strat,
							TrackedTransaction = new TrackedTransaction()
							{
								Transaction = txPayload.Object
							}
						}
					});
				}

				foreach(var pubkey in pubKeys)
				{
					Notify(pubkey.Strat, true);
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

		private void Notify(IDerivationStrategy pubkey, bool log)
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

		private HashSet<DerivationStrategyWrapper> GetInterestedWallets(Transaction tx)
		{
			var pubKeys = new HashSet<DerivationStrategyWrapper>();
			tx.CacheHashes();
			foreach(var input in tx.Inputs)
			{
				var signer = input.ScriptSig.GetSigner() ?? input.WitScript.ToScript().GetSigner();
				if(signer != null)
				{
					var keyInfo = Runtime.Repository.GetKeyInformation(signer.ScriptPubKey);
					if(keyInfo != null)
					{
						pubKeys.Add(new DerivationStrategyWrapper(keyInfo.RootKey));
						Runtime.Repository.MarkAsUsed(keyInfo);
					}
				}
			}

			foreach(var output in tx.Outputs)
			{
				var keyInfo = Runtime.Repository.GetKeyInformation(output.ScriptPubKey);
				if(keyInfo != null)
				{
					pubKeys.Add(new DerivationStrategyWrapper(keyInfo.RootKey));
					Runtime.Repository.MarkAsUsed(keyInfo);
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
