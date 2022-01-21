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
	public class FullySynchedEvent 
	{
		public FullySynchedEvent(NBXplorerNetwork network)
		{
			Network = network;
		}

		public NBXplorerNetwork Network { get; }
	}
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

		public BlockLocator CurrentLocation { get; private set; }

		protected override void AttachCore()
		{
			AttachedNode.StateChanged += AttachedNode_StateChanged;
			AttachedNode.MessageReceived += AttachedNode_MessageReceived;
			if (AttachedNode.State == NodeState.HandShaked)
				NodeHandshaked(AttachedNode);
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
		}
		private void AttachedNode_MessageReceived(Node node, IncomingMessage message)
		{
			if (message.Message.Payload is InvPayload invs)
			{
				// Do not asks transactions if we are synching so that we can process blocks faster
				if (IsSynching())
					return;
				var data = new GetDataPayload();
				foreach (var inv in invs.Inventory.Where(t => t.Type.HasFlag(InventoryType.MSG_TX)))
				{
					inv.Type = node.AddSupportedOptions(inv.Type);
					data.Inventory.Add(inv);
				}
				if (data.Inventory.Count != 0)
					node.SendMessageAsync(data);
			}
			else if (message.Message.Payload is HeadersPayload headers)
			{
				if (headers.Headers.Count == 0)
					return;
				_NewBlock.Set();
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
				NodeHandshaked(node);
			}
			if (node.State == NodeState.Offline)
				Logs.Explorer.LogInformation($"{Network.CryptoCode}: Closed connection with node");
			if (node.State == NodeState.Failed)
				Logs.Explorer.LogError($"{Network.CryptoCode}: Connection unexpectedly failed: {node.DisconnectReason.Reason}");
		}

		Task _BlockLoop;
		private void NodeHandshaked(Node node)
		{
			if (_BlockLoop != null)
				return;
			Logs.Explorer.LogInformation($"{Network.CryptoCode}: Handshaked node");
			node.SendMessageAsync(new MempoolPayload());
			_BlockLoop = IndexBlockLoop(node, _Cts.Token);
		}

		Signaler _NewBlock = new Signaler();
		private async Task IndexBlockLoop(Node node, CancellationToken cancellationToken)
		{
			try
			{
				CurrentLocation = await Repository.GetIndexProgress() ?? GetDefaultCurrentLocation();
				var fork = Chain.FindFork(CurrentLocation);
				if (fork == null)
				{
					CurrentLocation = GetDefaultCurrentLocation();
					fork = Chain.FindFork(CurrentLocation);
				}
				Logs.Explorer.LogInformation($"{Network.CryptoCode}: Starting scan at block " + fork.Height);

				var downloader = new BlockDownloader(Chain, node);

				while (true)
				{
					int downloaded = 0;
					Block lastBlock = null;
					await foreach (var block in downloader.DownloadBlocks(CurrentLocation, cancellationToken))
					{
						await SaveMatches(block);
						downloaded++;
						if (downloaded % 5 == 0)
						{
							CurrentLocation = Chain.GetLocator(block.Header.GetHash()) ?? CurrentLocation;
							await Repository.SetIndexProgress(CurrentLocation);
						}
						lastBlock = block;
					}
					if (lastBlock != null)
					{
						CurrentLocation = Chain.GetLocator(lastBlock.Header.GetHash()) ?? CurrentLocation;
						await Repository.SetIndexProgress(CurrentLocation);
					}
					if (CurrentLocation.Blocks.Count > 0 && CurrentLocation.Blocks[0] == Chain.TipBlock.Hash)
						_EventAggregator.Publish(new FullySynchedEvent(Network), true);
					await _NewBlock.Wait(cancellationToken);
				}
			}
			catch when (cancellationToken.IsCancellationRequested)
			{
			}
			catch (Exception ex)
			{
				Logs.Explorer.LogError($"{Network.CryptoCode}: Unhandled error in IndexBlockLoop");
				Logs.Explorer.LogError(ex.ToString());
				node.DisconnectAsync($"{Network.CryptoCode}: Unhandled error in IndexBlockLoop", ex);
			}
		}
	}
}
