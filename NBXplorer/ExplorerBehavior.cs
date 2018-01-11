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
		public ExplorerBehavior(Repository repo, ConcurrentChain chain, EventAggregator eventAggregator)
		{
			if(repo == null)
				throw new ArgumentNullException(nameof(repo));
			if(chain == null)
				throw new ArgumentNullException(nameof(chain));
			_Chain = chain;
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

		private readonly ConcurrentChain _Chain;
		private readonly Repository _Repository;

		public ConcurrentChain Chain
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
			return new ExplorerBehavior(_Repository, _Chain, _EventAggregator) { StartHeight = StartHeight };
		}

		Timer _Timer;

		public BlockLocator CurrentLocation
		{
			get
			{
				return _CurrentLocation;
			}
		}

		protected override void AttachCore()
		{
			AttachedNode.StateChanged += AttachedNode_StateChanged;
			AttachedNode.MessageReceived += AttachedNode_MessageReceived;
			_CurrentLocation = Repository.GetIndexProgress() ?? GetDefaultCurrentLocation();
			var fork = Chain.FindFork(_CurrentLocation);
			if(fork == null)
			{
				_CurrentLocation = GetDefaultCurrentLocation();
				fork = Chain.FindFork(_CurrentLocation);
			}
			Logs.Explorer.LogInformation($"{Network.CryptoCode}: Starting scan at block " + fork.Height);
			_Timer = new Timer(Tick, null, 0, 30);
		}

		private BlockLocator GetDefaultCurrentLocation()
		{
			if(StartHeight > Chain.Height)
				throw new InvalidOperationException($"{Network.CryptoCode}: StartHeight should not be above the current tip");
			return StartHeight == -1 ?
				new BlockLocator() { Blocks = new List<uint256>() { Chain.Tip.HashBlock } } :
				new BlockLocator() { Blocks = new List<uint256>() { Chain.GetBlock(StartHeight).HashBlock } };
		}



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
			var currentLocation = _CurrentLocation;
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
					var blockHeader = Chain.GetBlock(block.Object.GetHash());
					if(blockHeader == null)
						return;
					var currentLocation = blockHeader.GetLocator();
					_CurrentLocation = currentLocation;
					if(_InFlights.TryRemove(block.Object.GetHash(), out o))
					{
						foreach(var tx in block.Object.Transactions)
							tx.CacheHashes();

						var matches =
							block.Object.Transactions
							.SelectMany(tx => GetMatches(tx))
							.ToArray();

						var blockHash = block.Object.GetHash();
						SaveMatches(matches, blockHash);
						//Save index progress everytimes if not synching, or once every 100 blocks otherwise
						if(!IsSynching() || blockHash.GetLow32() % 100 == 0)
							Repository.SetIndexProgress(currentLocation);
						_EventAggregator.Publish(new Events.NewBlockEvent(this._Repository.Network.CryptoCode, blockHash));
					}
					if(_InFlights.Count == 0)
						AskBlocks();
				}
			});

			message.Message.IfPayloadIs<TxPayload>(txPayload =>
			{
				var matches = GetMatches(txPayload.Object).ToArray();
				SaveMatches(matches, null);
			});

		}

		private void SaveMatches(TransactionMatch[] matches, uint256 h)
		{
			Repository.MarkAsUsed(matches.SelectMany(m => m.Outputs).ToArray());
			Repository.SaveMatches(matches.Select(m => m.CreateInsertTransaction(h)).ToArray());
			var saved = Repository.SaveTransactions(matches.Select(m => m.Transaction).Distinct().ToArray(), h);
			for(int i = 0; i < matches.Length; i++)
			{
				_EventAggregator.Publish(new NewTransactionMatchEvent(this._Repository.Network.CryptoCode,h, matches[i], saved[i]));
			}
		}

		public bool IsSynching()
		{
			var location = _CurrentLocation;
			if(location == null)
				return true;
			var fork = Chain.FindFork(location);
			return Chain.Tip.Height - fork.Height > 10;
		}

		private IEnumerable<TransactionMatch> GetMatches(Transaction tx)
		{
			var matches = new Dictionary<DerivationStrategyBase, TransactionMatch>();
			HashSet<Script> scripts = new HashSet<Script>();
			foreach(var input in tx.Inputs)
			{
				var signer = input.ScriptSig.GetSigner() ?? input.WitScript.ToScript().GetSigner();
				if(signer != null)
				{
					scripts.Add(signer.ScriptPubKey);
				}
			}

			int scriptPubKeyIndex = scripts.Count;
			foreach(var output in tx.Outputs)
			{
				scripts.Add(output.ScriptPubKey);
			}

			var keyInformations = Repository.GetKeyInformations(scripts.ToArray());
			for(int scriptIndex = 0; scriptIndex < keyInformations.Length; scriptIndex++)
			{
				for(int i = 0; i < keyInformations[scriptIndex].Length; i++)
				{
					var keyInfo = keyInformations[scriptIndex][i];
					if(!matches.TryGetValue(keyInfo.DerivationStrategy, out TransactionMatch match))
					{
						match = new TransactionMatch();
						matches.Add(keyInfo.DerivationStrategy, match);
						match.DerivationStrategy = keyInfo.DerivationStrategy;
						match.Transaction = tx;
					}
					var isOutput = scriptIndex >= scriptPubKeyIndex;
					if(isOutput)
						match.Outputs.Add(keyInfo);
					else
						match.Inputs.Add(keyInfo);
				}
			}
			return matches.Values;
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
