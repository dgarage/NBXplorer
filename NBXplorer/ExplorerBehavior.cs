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
			Task.Run(Init);
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
			if (_InFlights.Count != 0)
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


			var invs = Enumerable.Range(0, 50)
				.Select(i => Chain.GetBlock(i + currentBlock.Height + 1))
				.Where(_ => _ != null)
				.Take(40)
				.Select(b => new InventoryVector(node.AddSupportedOptions(InventoryType.MSG_BLOCK), b.Hash))
				.Where(b => _InFlights.TryAdd(b.Hash, new Download()))
				.ToArray();

			if (invs.Length != 0)
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
				Task.Run(() => SaveMatches(block.Object));
			}
			else if (message.Message.Payload is TxPayload txPayload)
			{
				Task.Run(() => SaveMatches(txPayload.Object));
			}
		}

		private async Task SaveMatches(Block block)
		{
			block.Header.PrecomputeHash(false, false);
			Download o;
			if (_InFlights.ContainsKey(block.GetHash()))
			{
				var currentLocation = Chain.GetLocator(block.GetHash());
				if (currentLocation == null)
					return;
				CurrentLocation = currentLocation;
				if (_InFlights.TryRemove(block.GetHash(), out o))
				{
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
					//Save index progress everytimes if not synching, or once every 100 blocks otherwise
					if (!IsSynching() || blockHash.GetLow32() % 100 == 0)
						await Repository.SetIndexProgress(currentLocation);
					var slimBlockHeader = Chain.GetBlock(blockHash);
					if (slimBlockHeader != null)
						_EventAggregator.Publish(new Models.NewBlockEvent()
						{
							CryptoCode = _Repository.Network.CryptoCode,
							Hash = blockHash,
							Height = slimBlockHeader.Height,
							PreviousBlockHash = slimBlockHeader.Previous
						});
				}
				if (_InFlights.Count == 0)
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
			for (int i = 0; i < matches.Length; i++)
			{
				_EventAggregator.Publish(new Models.NewTransactionEvent()
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
				});
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
