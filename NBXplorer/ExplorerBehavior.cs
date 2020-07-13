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
using Microsoft.AspNetCore.Mvc.Formatters;

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

		CancellationTokenSource _Cts = new CancellationTokenSource();
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
		}

		public async Task Init()
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
			_Timer = new Timer(Tick, null, 0, (int)TimeSpan.FromSeconds(30).TotalMilliseconds);
		}

		private BlockLocator GetDefaultCurrentLocation()
		{
			if (StartHeight > Chain.Height)
				throw new InvalidOperationException($"{Network.CryptoCode}: StartHeight should not be above the current tip");

			BlockLocator blockLocator = null;
			if (StartHeight == -1)
			{
				blockLocator = Chain.GetTipLocator();
				Logs.Explorer.LogInformation($"{Network.CryptoCode}: Current Index Progress not found, start syncing from the header's chain tip (At height: {Chain.Height})");
			}
			else
			{
				blockLocator = Chain.GetLocator(StartHeight);
				Logs.Explorer.LogInformation($"{Network.CryptoCode}: Current Index Progress not found, start syncing at height {Chain.Height}");
			}
			return blockLocator;
		}



		int AskBlocks()
		{
			if (!_InFlights.IsEmpty)
				return 0;
			var node = AttachedNode;
			if (node == null || node.State != NodeState.HandShaked)
				return 0;
			if (Chain.Height < node.PeerVersion.StartHeight)
				return 0;
			var currentLocation = (_HighestInFlight ?? CurrentLocation);
			if (currentLocation == null)
				return 0;
			var currentBlock = Chain.FindFork(currentLocation);
			if (currentBlock.Height < StartHeight)
				currentBlock = Chain.GetBlock(StartHeight) ?? Chain.TipBlock;

			//Up to date
			if (Chain.TipBlock.Hash == currentBlock.Hash)
				return 0;


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
				_HighestInFlight = _HighestInFlight ?? Chain.GetLocator(invs[invs.Count - 1].Hash);
				node.SendMessageAsync(new GetDataPayload(invs.ToArray()));
				if (invs.Count > 1)
					GC.Collect(); // Let's collect memory if we are synching 
			}
			return invs.Count;
		}

		ConcurrentDictionary<uint256, uint256> _InFlights = new ConcurrentDictionary<uint256, uint256>();
		BlockLocator _HighestInFlight;
		DateTimeOffset? _LastBlockDownloaded;
		readonly static TimeSpan DownloadHangingTimeout = TimeSpan.FromMinutes(1.0);
		void Tick(object state)
		{
			try
			{
				if (AskBlocks() == 0 &&
					IsHanging())
				{
					Logs.Explorer.LogInformation($"{Network.CryptoCode}: Block download seems to hang, let's reconnect to this node");
					AttachedNode?.DisconnectAsync("Block download is hanging");
				}
			}
			catch (Exception ex)
			{
				if (AttachedNode == null)
					return;
				Logs.Explorer.LogError($"{Network.CryptoCode}: Exception in ExplorerBehavior tick loop");
				Logs.Explorer.LogError(ex.ToString());
			}
		}

		public bool IsDownloading => !_InFlights.IsEmpty;

		public bool IsHanging()
		{
			var node = AttachedNode;
			if (node == null)
				return false;
			DateTimeOffset lastActivity;
			if (IsDownloading) // If we download
			{
				if (_LastBlockDownloaded is DateTimeOffset lastBlockDownloaded)
					lastActivity = lastBlockDownloaded;
				else
					lastActivity = node.ConnectedAt;
			}
			else if (IsSynching()) // Not downloading but synching
			{
				lastActivity = node.ConnectedAt;
			}
			else // If we do not download and we do not sync, we are ok
			{
				return false;
			}
			return (DateTimeOffset.UtcNow - lastActivity) >= DownloadHangingTimeout;
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
			_Cts.Cancel();
			_Timer?.Dispose();
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
				Run(() => SaveMatches(txPayload.Object, true));
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
					this.AttachedNode?.DisconnectAsync($"{Network.CryptoCode}: Unhandled error while treating a message", ex);
				}
			});
		}
		private async Task SaveMatches(Block block)
		{
			_LastBlockDownloaded = DateTimeOffset.UtcNow;
			block.Header.PrecomputeHash(false, false);
			foreach (var tx in block.Transactions)
				tx.PrecomputeHash(false, true);

			var blockHash = block.GetHash();
			var delay = TimeSpan.FromSeconds(1);
		retry:
			try
			{
				DateTimeOffset now = DateTimeOffset.UtcNow;
				var matches =
					(await Repository.GetMatches(block.Transactions, blockHash, now, true))
					.ToArray();
				await SaveMatches(matches, blockHash, now, true);
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
					await Repository.SaveEvent(blockEvent);
					_EventAggregator.Publish(blockEvent);
					_EventAggregator.Publish(new RawBlockEvent(block, this.Network), true);
				}
			}
			catch (ObjectDisposedException)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logs.Explorer.LogWarning(ex, $"{Network.CryptoCode}: Error while saving block in database, retrying in {delay.TotalSeconds} seconds ({ex.Message})");
				await Task.Delay(delay, _Cts.Token);
				delay = delay * 2;
				var maxDelay = TimeSpan.FromSeconds(60);
				if (delay > maxDelay)
					delay = maxDelay;
				goto retry;
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


		internal async Task SaveMatches(Transaction transaction, bool fireEvents)
		{
			var now = DateTimeOffset.UtcNow;
			var matches = (await Repository.GetMatches(transaction, null, now, false)).ToArray();
			await SaveMatches(matches, null, now, fireEvents);
		}

		private async Task SaveMatches(TrackedTransaction[] matches, uint256 blockHash, DateTimeOffset now, bool fireEvents)
		{
			await Repository.SaveMatches(matches);
			_ = AddressPoolService.GenerateAddresses(Network, matches);
			var saved = await Repository.SaveTransactions(now, matches.Select(m => m.Transaction).Distinct().ToArray(), blockHash);
			var savedTransactions = saved.ToDictionary(s => s.Transaction.GetHash());

			int? maybeHeight = null;
			var chainHeight = Chain.Height;
			if (blockHash != null && Chain.TryGetHeight(blockHash, out int height))
			{
				maybeHeight = height;
			}
			if (fireEvents)
			{
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
