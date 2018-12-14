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
using NBXplorer.DerivationStrategy;
using System.Net.Http;
using NBXplorer.Models;
using NBXplorer.Events;
using NBXplorer.Configuration;

namespace NBXplorer
{
	public class ExplorerBehavior : NodeBehavior
	{
		public ExplorerBehavior(Repository repo, SlimChain chain, AddressPoolService addressPoolService, EventAggregator eventAggregator)
		{
			if (repo == null)
				throw new ArgumentNullException(nameof(repo));
			if (chain == null)
				throw new ArgumentNullException(nameof(chain));
			if (addressPoolService == null)
				throw new ArgumentNullException(nameof(addressPoolService));
			_Chain = chain;
			AddressPoolService = addressPoolService;
			_Repository = repo;
			_EventAggregator = eventAggregator;
		}

		EventAggregator _EventAggregator;

		Repository Repository
		{
			get
			{
				return _Repository;
			}
		}

		private readonly SlimChain _Chain;
		private readonly Repository _Repository;

		public SlimChain Chain
		{
			get
			{
				return _Chain;
			}
		}
		public int StartHeight
		{
			get;
			set;
		}

		public override object Clone()
		{
			return new ExplorerBehavior(_Repository, _Chain, AddressPoolService, _EventAggregator) { StartHeight = StartHeight };
		}

		Timer _Timer;

		public BlockLocator CurrentLocation { get; private set; }

		protected override void AttachCore()
		{
			AttachedNode.StateChanged += AttachedNode_StateChanged;
			AttachedNode.MessageReceived += AttachedNode_MessageReceived;
			Run(Init);
			_Timer = new Timer(Tick, null, 0, (int)TimeSpan.FromSeconds(30).TotalMilliseconds);
		}

		private async Task Init()
		{
			var currentLocation = await Repository.GetIndexProgress() ?? GetDefaultCurrentLocation();
			var fork = Chain.FindFork(currentLocation);
			if (fork == null)
			{
				currentLocation = GetDefaultCurrentLocation();
				fork = Chain.FindFork(currentLocation);
			}
			CurrentLocation = currentLocation;
			Logs.Explorer.LogInformation($"{Network.CryptoCode}: Starting scan at block " + fork.Height);
		}

		private BlockLocator GetDefaultCurrentLocation()
		{
			if (StartHeight > Chain.Height)
				throw new InvalidOperationException($"{Network.CryptoCode}: StartHeight should not be above the current tip");
			return StartHeight == -1 ?
				Chain.GetTipLocator() :
				Chain.GetLocator(StartHeight);
		}



		public void AskBlocks()
		{
			var node = AttachedNode;
			if (node == null || node.State != NodeState.HandShaked)
				return;
			if (Chain.Height < node.PeerVersion.StartHeight)
				return;
			var currentLocation = CurrentLocation;
			if (currentLocation == null)
				return;
			var currentBlock = Chain.FindFork(currentLocation);
			if (currentBlock.Height < StartHeight)
				currentBlock = Chain.GetBlock(StartHeight) ?? Chain.TipBlock;

			//Up to date
			if (Chain.TipBlock.Hash == currentBlock.Hash)
				return;


			int maxConcurrentBlocks = 10;
			List<InventoryVector> invs = new List<InventoryVector>();
			foreach (var i in Enumerable.Range(0, int.MaxValue))
			{
				var block = Chain.GetBlock(currentBlock.Height + 1 + i);
				if (block == null)
					break;
				if (_InFlights.TryAdd(block.Hash, block.Hash))
					invs.Add(new InventoryVector(node.AddSupportedOptions(InventoryType.MSG_BLOCK), block.Hash));
				if (invs.Count == maxConcurrentBlocks)
					break;
			}

			if (invs.Count != 0)
			{
				Repository.BatchSize = invs.Count == maxConcurrentBlocks ? int.MaxValue : 100;
				_HighestInFlight = Chain.GetLocator(invs[invs.Count - 1].Hash);
				node.SendMessageAsync(new GetDataPayload(invs.ToArray()));
			}
		}

		ConcurrentDictionary<uint256, uint256> _InFlights = new ConcurrentDictionary<uint256, uint256>();
		BlockLocator _HighestInFlight;

		void Tick(object state)
		{
			try
			{
				AskBlocks();
			}
			catch (Exception ex)
			{
				if (AttachedNode == null)
					return;
				Logs.Explorer.LogError($"{Network.CryptoCode}: Exception in ExplorerBehavior tick loop");
				Logs.Explorer.LogError(ex.ToString());
			}
		}

		public NBXplorerNetwork Network
		{
			get
			{
				return _Repository.Network;
			}
		}

		public AddressPoolService AddressPoolService
		{
			get;
		}

		protected override void DetachCore()
		{
			AttachedNode.StateChanged -= AttachedNode_StateChanged;
			AttachedNode.MessageReceived -= AttachedNode_MessageReceived;

			_Timer.Dispose();
			_Timer = null;
		}
		private void AttachedNode_MessageReceived(Node node, IncomingMessage message)
		{
			if (message.Message.Payload is InvPayload invs)
			{
				// Do not asks transactions if we are synching so that we can process blocks faster
				if (IsSynching())
					return;
				var data = new GetDataPayload();
				foreach (var inv in invs.Inventory)
				{
					inv.Type = node.AddSupportedOptions(inv.Type);
					if (inv.Type.HasFlag(InventoryType.MSG_TX))
						data.Inventory.Add(inv);
				}
				if (data.Inventory.Count != 0)
					node.SendMessageAsync(data);
			}
			else if (message.Message.Payload is HeadersPayload headers)
			{
				if (headers.Headers.Count == 0)
					return;
				AskBlocks();
			}
			else if (message.Message.Payload is BlockPayload block)
			{
				Run(() => SaveMatches(block.Object));
			}
			else if (message.Message.Payload is TxPayload txPayload)
			{
				Run(() => SaveMatches(txPayload.Object));
			}
		}
		Task Run(Func<Task> act)
		{
			return Task.Run(async () =>
			{
				try
				{
					await act();
				}
				catch (Exception ex)
				{
					Logs.Explorer.LogError($"{Network.CryptoCode}: Unhandled error while treating a message");
					Logs.Explorer.LogError(ex.ToString());
					this.AttachedNode.DisconnectAsync($"{Network.CryptoCode}: Unhandled error while treating a message", ex);
				}
			});
		}
		private async Task SaveMatches(Block block)
		{
			block.Header.PrecomputeHash(false, false);
			foreach (var tx in block.Transactions)
				tx.PrecomputeHash(false, true);

			var blockHash = block.GetHash();
			DateTimeOffset now = DateTimeOffset.UtcNow;
			var matches =
				block.Transactions
				.Select(tx => Repository.GetMatches(tx, blockHash, now))
				.ToArray();
			await Task.WhenAll(matches);
			await SaveMatches(matches.SelectMany((Task<TrackedTransaction[]> m) => m.GetAwaiter().GetResult()).ToArray(), blockHash, now);
			var slimBlockHeader = Chain.GetBlock(blockHash);
			if (slimBlockHeader != null)
			{
				var blockEvent = new Models.NewBlockEvent()
				{
					CryptoCode = _Repository.Network.CryptoCode,
					Hash = blockHash,
					Height = slimBlockHeader.Height,
					PreviousBlockHash = slimBlockHeader.Previous
				};
				var saving = Repository.SaveEvent(blockEvent);
				_EventAggregator.Publish(blockEvent);
				await saving;
			}

			if (_InFlights.TryRemove(blockHash, out var unused) && _InFlights.IsEmpty)
			{
				var highestInFlight = _HighestInFlight;
				_HighestInFlight = null;
				if (highestInFlight != null)
				{
					CurrentLocation = highestInFlight;
					await Repository.SetIndexProgress(highestInFlight);
				}
				AskBlocks();
			}
		}


		private async Task SaveMatches(Transaction transaction)
		{
			var now = DateTimeOffset.UtcNow;
			var matches = (await Repository.GetMatches(transaction, null, now)).ToArray();
			await SaveMatches(matches, null, now);
		}

		private async Task SaveMatches(TrackedTransaction[] matches, uint256 blockHash, DateTimeOffset now)
		{
			await Repository.SaveMatches(matches);
			AddressPoolService.RefillAddressPoolIfNeeded(Network, matches);
			var saved = await Repository.SaveTransactions(now, matches.Select(m => m.Transaction).Distinct().ToArray(), blockHash);
			var savedTransactions = saved.ToDictionary(s => s.Transaction.GetHash());

			int? maybeHeight = null;
			var chainHeight = Chain.Height;
			if (blockHash != null && Chain.TryGetHeight(blockHash, out int height))
			{
				maybeHeight = height;
			}
			Task[] saving = new Task[matches.Length];
			for (int i = 0; i < matches.Length; i++)
			{
				var txEvt = new Models.NewTransactionEvent()
				{
					TrackedSource = matches[i].TrackedSource,
					DerivationStrategy = (matches[i].TrackedSource is DerivationSchemeTrackedSource dsts) ? dsts.DerivationStrategy : null,
					CryptoCode = Network.CryptoCode,
					BlockId = blockHash,
					TransactionData = new TransactionResult()
					{
						BlockId = blockHash,
						Height = maybeHeight,
						Confirmations = maybeHeight == null ? 0 : chainHeight - maybeHeight.Value + 1,
						Timestamp = now,
						Transaction = matches[i].Transaction,
						TransactionHash = matches[i].TransactionHash
					},
					Outputs = matches[i].GetReceivedOutputs().ToList()
				};
				saving[i] = Repository.SaveEvent(txEvt);
				_EventAggregator.Publish(txEvt);
			}
			await Task.WhenAll(saving);
		}
		public bool IsSynching()
		{
			var location = CurrentLocation;
			if (location == null)
				return true;
			var fork = Chain.FindFork(location);
			return Chain.Height - fork.Height > 10;
		}

		private void AttachedNode_StateChanged(Node node, NodeState oldState)
		{
			if (node.State == NodeState.HandShaked)
			{
				Logs.Explorer.LogInformation($"{Network.CryptoCode}: Handshaked node");
				node.SendMessageAsync(new MempoolPayload());
				AskBlocks();
			}
			if (node.State == NodeState.Offline)
				Logs.Explorer.LogInformation($"{Network.CryptoCode}: Closed connection with node");
			if (node.State == NodeState.Failed)
				Logs.Explorer.LogError($"{Network.CryptoCode}: Connection unexpectedly failed: {node.DisconnectReason.Reason}");
		}
	}
}
