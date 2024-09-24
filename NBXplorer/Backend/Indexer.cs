using Dapper;
using Microsoft.Extensions.Logging;
using NBitcoin.Protocol.Behaviors;
using NBitcoin.Protocol;
using NBitcoin.RPC;
using NBitcoin;
using NBXplorer.Configuration;
using NBXplorer.Events;
using NBXplorer.Models;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace NBXplorer.Backend
{
	public class Indexer
	{
		public Indexer(
			AddressPoolService addressPoolService,
			ILogger logger,
			NBXplorerNetwork network,
			RPCClient rpcClient,
			Repository repository,
			DbConnectionFactory connectionFactory,
			ExplorerConfiguration explorerConfiguration,
			ChainConfiguration chainConfiguration,
			EventAggregator eventAggregator)
		{
			AddressPoolService = addressPoolService;
			Logger = logger;
			this.network = network;
			RPCClient = rpcClient;
			Repository = repository;
			ConnectionFactory = connectionFactory;
			ExplorerConfiguration = explorerConfiguration;
			ChainConfiguration = chainConfiguration;
			EventAggregator = eventAggregator;
		}
		CancellationTokenSource cts;
		Task _indexerLoop;
		Task _watchdogLoop;

		// This one will check if the indexer is "stuck" and disconnect the node if it is the case
		async Task WatchdogLoop()
		{
			var cancellationToken = cts.Token;
			wait:
			try
			{
				await Task.Delay(TimeSpan.FromMinutes(5.0), cancellationToken);
				var lastBlock = await SeemsStuck(cancellationToken);
				if (lastBlock is null)
					goto wait;
				await Task.Delay(TimeSpan.FromMinutes(2.0), cancellationToken);
				var lastBlock2 = await SeemsStuck(cancellationToken);
				if (lastBlock != lastBlock2)
					goto wait;
				_Connection?.Dispose($"Sync seems stuck after block {lastBlock.Hash} ({lastBlock.Hash}), restarting the connection.");
				goto wait;
			}
			catch when (cts.Token.IsCancellationRequested)
			{
				goto end;
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Unhandled exception in the indexer watchdog");
				goto wait;
			}
			end:;
		}

		async Task<SlimChainedBlock> SeemsStuck(CancellationToken cancellationToken)
		{
			if (State is not (BitcoinDWaiterState.NBXplorerSynching or BitcoinDWaiterState.Ready) ||
						lastIndexedBlock is not { } lastBlock ||
						GetConnectedClient() is not RPCClient rpc)
			{
				return null;
			}
			var blockchainInfo = await rpc.GetBlockchainInfoAsyncEx(cancellationToken);
			return blockchainInfo.BestBlockHash != lastBlock.Hash ? lastBlock : null;
		}

		async Task IndexerLoop()
		{
			TimeSpan retryDelay = TimeSpan.FromSeconds(0);
			retry:
			try
			{
				await IndexerLoopCore(cts.Token);
				if (!cts.Token.IsCancellationRequested)
					goto retry;
			}
			catch when (cts.Token.IsCancellationRequested)
			{
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, $"Unhandled exception in the indexer, retrying in {retryDelay.TotalSeconds} seconds");
				try
				{
					await Task.Delay(retryDelay, cts.Token);
				}
				catch { }
				retryDelay += TimeSpan.FromSeconds(5.0);
				retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks, TimeSpan.FromMinutes(1.0).Ticks));
				goto retry;
			}
		}

		class Connection : IDisposable
		{
			public Channel<Object> Events;
			public Channel<Block> Blocks;
			public Node Node;
			public Connection(Node node)
			{
				Node = node;
				Events = Channel.CreateUnbounded<object>(new() { AllowSynchronousContinuations = false });
				Blocks = Channel.CreateUnbounded<Block>(new() { AllowSynchronousContinuations = false });
			}
			bool _Disposed = false;

			public void Dispose()
			{
				Dispose(null);
			}
			public void Dispose(string reason)
			{
				if (_Disposed)
					return;
				Node.DisconnectAsync(reason);
				Events.Writer.TryComplete();
				Blocks.Writer.TryComplete();
				_Disposed = true;
			}
		}
		Connection _Connection;
		private async Task IndexerLoopCore(CancellationToken token)
		{
			await ConnectNode(token);
			var connection = _Connection;
			await foreach (var item in connection.Events.Reader.ReadAllAsync(token))
			{
				await using var conn = await ConnectionFactory.CreateConnectionHelper(Network);
				if (item is PullBlocks pb)
				{
					var headers = ConsolidatePullBlocks(connection.Events.Reader, pb);
					foreach (var batch in headers.Chunk(maxinflight))
					{
						_ = connection.Node.SendMessageAsync(
							new GetDataPayload(
								batch.Select(b => new InventoryVector(connection.Node.AddSupportedOptions(InventoryType.MSG_BLOCK), b.GetHash())
								).ToArray()));
						var remaining = batch.Select(b => b.GetHash()).ToHashSet();
						List<Block> unorderedBlocks = new List<Block>();
						await foreach (var block in connection.Blocks.Reader.ReadAllAsync(token))
						{
							if (!remaining.Remove(block.Header.GetHash()))
								continue;
							if (lastIndexedBlock is null || block.Header.HashPrevBlock == lastIndexedBlock.Hash)
							{
								SlimChainedBlock slimChainedBlock = lastIndexedBlock is null ?
									(await RPCClient.GetBlockHeaderAsyncEx(block.Header.GetHash(), token))?.ToSlimChainedBlock() :
									new SlimChainedBlock(block.Header.GetHash(), lastIndexedBlock.Hash, lastIndexedBlock.Height + 1);
								await SaveMatches(conn, block, slimChainedBlock);
							}
							else
							{
								unorderedBlocks.Add(block);
							}
							if (remaining.Count == 0)
							{
								// There are two reasons to receive unordered blocks:
								//   1. There is a fork.
								//   2. Node decides to send headers without asking.
								if (unorderedBlocks.Count > 0)
								{
									Task<RPCBlockHeader>[] slimChainedBlocks = new Task<RPCBlockHeader>[unorderedBlocks.Count];
									var rpcBatch = RPCClient.PrepareBatch();
									for (int i = 0; i < unorderedBlocks.Count; i++)
									{
										slimChainedBlocks[i] = rpcBatch.GetBlockHeaderAsyncEx(unorderedBlocks[i].GetHash(), token);
									}
									await rpcBatch.SendBatchAsync();
									// If there is a fork, we should index the unordered blocks
									bool unconfedBlocks = false;
									bool fork = await RPCClient.GetBlockHeaderAsyncEx(lastIndexedBlock.Hash, token) == null;
									foreach (var b in Enumerable.Zip(unorderedBlocks, slimChainedBlocks)
													.Where(b => fork || b.Second.Result.Height > lastIndexedBlock.Height)
													.OrderBy(b => b.Second.Result.Height)
													.ToList())
									{
										var slimBlock = await b.Second;
										if (fork && !unconfedBlocks)
										{
											await conn.MakeOrphanFrom(slimBlock.Height);
											unconfedBlocks = true;
										}
										await SaveMatches(conn, b.First, slimBlock.ToSlimChainedBlock());
									}
								}
								break;
							}
						}
						await SaveProgress(conn);
						await UpdateState(connection.Node);
					}
					if (connection.Node.State != NodeState.HandShaked)
						await AskNextHeaders(connection.Node, token);
				}
				if (item is Transaction tx)
				{
					var txs = PullTransactions(connection.Events.Reader, tx);
					await SaveMatches(conn, txs, null, true);
				}
			}
		}

		// Attempt to pull as much non-conflicting transactions as possible in one batch
		private List<Transaction> PullTransactions(ChannelReader<object> reader, Transaction tx)
		{
			List<Transaction> txs = new List<Transaction>();
			HashSet<OutPoint> spent = new HashSet<OutPoint>(tx.Inputs.Capacity);
			bool EnsureNoConflict(Transaction tx)
			{
				foreach (var i in tx.Inputs.Select(i => i.PrevOut))
					if (!spent.Add(i))
						return false;
				return true;
			}
			EnsureNoConflict(tx);
			txs.Add(tx);

			while (reader.TryPeek(out var p) && p is Transaction tx2)
			{
				if (!EnsureNoConflict(tx2))
					break;
				txs.Add(tx2);
				reader.TryRead(out _);
			}
			return txs;
		}

		// We sometimes receive burst of blocks, with some dups.
		// This method will pump as much headers from the channel as possible, removing the dups
		// along the way.
		private IList<BlockHeader> ConsolidatePullBlocks(ChannelReader<object> reader, PullBlocks pb)
		{
			List<PullBlocks> requests = new List<PullBlocks>();
			requests.Add(pb);
			while (reader.TryPeek(out var p) && p is PullBlocks pb2)
			{
				reader.TryRead(out _);
				requests.Add(pb2);
			}

			var headerCount = requests.Select(r => r.headers.Count).Sum();
			HashSet<uint256> blocks = new HashSet<uint256>(headerCount);
			List<BlockHeader> result = new List<BlockHeader>(headerCount);
			foreach (var h in requests.SelectMany(r => r.headers))
			{
				h.PrecomputeHash(false, true);
				if (blocks.Add(h.GetHash()))
					result.Add(h);
			}
			return result;
		}


		private async Task ConnectNode(CancellationToken token)
		{
			State = BitcoinDWaiterState.NotStarted;
			using (var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
			{
				var userAgent = "NBXplorer-" + RandomUtils.GetInt64();
				var nodeParams = new NodeConnectionParameters()
				{
					UserAgent = userAgent,
					ConnectCancellation = handshakeTimeout.Token,
					IsRelay = true
				};
				if (ExplorerConfiguration.SocksEndpoint != null)
				{
					var socks = new SocksSettingsBehavior()
					{
						OnlyForOnionHosts = false,
						SocksEndpoint = ExplorerConfiguration.SocksEndpoint
					};
					if (ExplorerConfiguration.SocksCredentials != null)
						socks.NetworkCredential = ExplorerConfiguration.SocksCredentials;
					nodeParams.TemplateBehaviors.Add(socks);
				}
				var node = await Node.ConnectAsync(network.NBitcoinNetwork, ChainConfiguration.NodeEndpoint, nodeParams);
				Logger.LogInformation($"TCP Connection succeed, handshaking...");
				node.VersionHandshake(handshakeTimeout.Token);
				Logger.LogInformation($"Handshaked");
				await node.SendMessageAsync(new SendHeadersPayload());

				await RPCArgs.TestRPCAsync(Network, RPCClient, token, Logger);
				if (await RPCClient.SupportTxIndex() is bool txIndex)
				{
					ChainConfiguration.HasTxIndex = txIndex;
				}
				if (ChainConfiguration.HasTxIndex)
				{
					Logger.LogInformation($"Has txindex support");
				}
				var peer = (await RPCClient.GetPeersInfoAsync())
									.FirstOrDefault(p => p.SubVersion == userAgent);
				if (peer.IsWhitelisted())
				{
					if (firstConnect)
					{
						firstConnect = false;
					}
					Logger.LogInformation($"NBXplorer is correctly whitelisted by the node");
				}
				else if (peer is null)
				{
					Logger.LogWarning($"{Network.CryptoCode}: The RPC server you are connecting to, doesn't seem to be the same server as the one providing the P2P connection. This is an untested setup and may have non-obvious side effects.");
				}
				else
				{
					var addressStr = peer.Address is IPEndPoint end ? end.Address.ToString() : peer.Address?.ToString();

					if (addressStr == "127.0.0.1" || addressStr == "::1")
					{
						Logger.LogInformation($"Connection is from localhost, no whitelist needed.");
					}
					else 
					{
						Logger.LogWarning($"{Network.CryptoCode}: Your NBXplorer server is not whitelisted by your node," +
						    $" you should add \"whitelist={addressStr}\" to the configuration file of your node. (Or use whitebind)");
					}
				}

				int waitTime = 10;

				// Need NetworkInfo for the get status
				NetworkInfo = await RPCClient.GetNetworkInfoAsync();
				retry:
				BlockchainInfo = await RPCClient.GetBlockchainInfoAsyncEx();
				if (BlockchainInfo.IsSynching(Network))
				{
					State = BitcoinDWaiterState.CoreSynching;
					await Task.Delay(waitTime * 2, token);
					waitTime = Math.Min(5_000, waitTime * 2);
					goto retry;
				}
				await RPCClient.EnsureWalletCreated(Logger);
				if (Network.NBitcoinNetwork.ChainName == ChainName.Regtest && !ChainConfiguration.NoWarmup)
				{
					if (await RPCClient.WarmupBlockchain(Logger))
						BlockchainInfo = await RPCClient.GetBlockchainInfoAsyncEx();
				}
				_NodeTip = (await RPCClient.GetBlockHeaderAsyncEx(BlockchainInfo.BestBlockHash, token))?.ToSlimChainedBlock();
				State = BitcoinDWaiterState.NBXplorerSynching;
				// Refresh the NetworkInfo that may have become different while it was synching.
				NetworkInfo = await RPCClient.GetNetworkInfoAsync();

				_Connection?.Dispose("Creating new connection");
				_Connection = new Connection(node);
				node.MessageReceived += Node_MessageReceived;
				node.Disconnected += Node_Disconnected;
				var locator = await AskNextHeaders(node, token);
				lastIndexedBlock = await Repository.GetLastIndexedSlimChainedBlock(locator);
				if (lastIndexedBlock is null)
				{
					var locatorTip = await RPCClient.GetBlockHeaderAsyncEx(locator.Blocks[0], token);
					lastIndexedBlock = locatorTip?.ToSlimChainedBlock();
				}
				await UpdateState(node);
			}
		}

		bool firstConnect = true;
		private async Task<BlockLocator> AskNextHeaders(Node node, CancellationToken token)
		{
			var indexProgress = await Repository.GetIndexProgress();
			if (indexProgress is null)
			{
				indexProgress = await GetDefaultCurrentLocation(token);
			}
			await node.SendMessageAsync(new GetHeadersPayload(indexProgress));
			return indexProgress;
		}

		static int[] BlockLocatorComposition = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 40, 80, 160, 320, 640, 1280, 2560, 5120, 10240, 20480, 40960 };
		private async Task SaveProgress(DbConnectionHelper conn)
		{
			// We pick blocks spaced exponentially from the the tip to build our block locator
			var heights = BlockLocatorComposition.Select(l => lastIndexedBlock.Height - l).ToArray();
			var blks = await conn.Connection.QueryAsync<string>(
			"SELECT blk_id FROM blks " +
			"WHERE code=@code AND height=ANY(@heights) AND confirmed IS TRUE " +
			"ORDER BY height DESC", new { code = Network.CryptoCode, heights });
			var locator = new BlockLocator();
			foreach (var b in blks)
				locator.Blocks.Add(uint256.Parse(b));
			await Repository.SetIndexProgress(conn.Connection, locator);
		}

		private async Task UpdateState(Node node)
		{
			if (node.State != NodeState.HandShaked)
				return;
			var blockchainInfo = await RPCClient.GetBlockchainInfoAsyncEx();
			if (blockchainInfo.IsSynching(Network))
			{
				State = BitcoinDWaiterState.CoreSynching;
			}
			else if (lastIndexedBlock != null)
			{
				int minBlock = 6;
				// Prevent some corner cases in tests, if we suddenly mine 200 blocks, we should still be synched on regtest
				if (Network.NBitcoinNetwork.ChainName == ChainName.Regtest)
					minBlock = 200;
				State = blockchainInfo.Headers - lastIndexedBlock.Height < minBlock ? BitcoinDWaiterState.Ready : BitcoinDWaiterState.NBXplorerSynching;
			}
		}

		private async Task<BlockLocator> GetDefaultCurrentLocation(CancellationToken token)
		{
			if (ChainConfiguration.StartHeight > BlockchainInfo.Headers)
				throw new InvalidOperationException($"{Network.CryptoCode}: StartHeight should not be above the current tip");
			BlockLocator blockLocator = null;
			if (ChainConfiguration.StartHeight == -1)
			{
				var bestBlock = await RPCClient.GetBestBlockHashAsync(token);
				var bh = await RPCClient.GetBlockHeaderAsyncEx(bestBlock, token);
				blockLocator = new BlockLocator();
				blockLocator.Blocks.Add(bh.Previous ?? bh.Hash);
				Logger.LogInformation($"Current Index Progress not found, start syncing from the header's chain tip (At height: {BlockchainInfo.Headers})");
			}
			else
			{
				var header = await RPCClient.GetBlockHeaderAsync(ChainConfiguration.StartHeight, token);
				var header2 = await RPCClient.GetBlockHeaderAsyncEx(header.GetHash(), token);
				blockLocator = new BlockLocator();
				blockLocator.Blocks.Add(header2.Previous ?? header2.Hash);
				Logger.LogInformation($"Current Index Progress not found, start syncing at height {ChainConfiguration.StartHeight}");
			}
			return blockLocator;
		}

		private async Task SaveMatches(DbConnectionHelper conn, Block block, SlimChainedBlock slimChainedBlock)
		{
			block.Header.PrecomputeHash(false, false);
			await SaveMatches(conn, block.Transactions, slimChainedBlock, true);
			EventAggregator.Publish(new RawBlockEvent(block, this.Network), true);
			lastIndexedBlock = slimChainedBlock;
		}

		SlimChainedBlock _NodeTip;

		private async Task SaveMatches(DbConnectionHelper conn, List<Transaction> transactions, SlimChainedBlock slimChainedBlock, bool fireEvents)
		{
			foreach (var tx in transactions)
				tx.PrecomputeHash(false, true);
			var now = DateTimeOffset.UtcNow;
			if (slimChainedBlock != null)
			{
				await conn.NewBlock(slimChainedBlock);
			}
			var matches = await Repository.GetMatchesAndSave(conn, transactions, slimChainedBlock, now, true);
			_ = AddressPoolService.GenerateAddresses(Network, matches);

			long confirmations = 0;
			if (slimChainedBlock != null)
			{
				if (slimChainedBlock.Height >= _NodeTip.Height)
					_NodeTip = slimChainedBlock;
				confirmations = _NodeTip.Height - slimChainedBlock.Height + 1;
				await conn.NewBlockCommit(slimChainedBlock.Hash);
				var blockEvent = new Models.NewBlockEvent()
				{
					CryptoCode = Network.CryptoCode,
					Hash = slimChainedBlock.Hash,
					Height = slimChainedBlock.Height,
					PreviousBlockHash = slimChainedBlock.Previous,
					Confirmations = confirmations
				};
				await Repository.SaveEvent(conn, blockEvent);
				EventAggregator.Publish(blockEvent);
			}
			if (fireEvents)
			{
				NewTransactionEvent[] evts = new NewTransactionEvent[matches.Length];
				for (int i = 0; i < matches.Length; i++)
				{
					var txEvt = new Models.NewTransactionEvent()
					{
						TrackedSource = matches[i].TrackedSource,
						DerivationStrategy = (matches[i].TrackedSource is DerivationSchemeTrackedSource dsts) ? dsts.DerivationStrategy : null,
						CryptoCode = Network.CryptoCode,
						BlockId = slimChainedBlock?.Hash,
						TransactionData = new TransactionResult()
						{
							BlockId = slimChainedBlock?.Hash,
							Height = slimChainedBlock?.Height,
							Confirmations = confirmations,
							Timestamp = now,
							Transaction = matches[i].Transaction,
							TransactionHash = matches[i].TransactionHash
						},
						Inputs = matches[i].MatchedInputs.OrderBy(m => m.InputIndex).ToList(),
						Outputs = matches[i].GetReceivedOutputs().ToList(),
						Replacing = matches[i].Replacing.ToList()
					};

					evts[i] = txEvt;
				}
				await Repository.SaveEvents(conn, evts);
				foreach (var ev in evts)
				{
					EventAggregator.Publish(ev);
				}
			}
		}

		SlimChainedBlock lastIndexedBlock;
		record PullBlocks(IList<BlockHeader> headers);
		private void Node_MessageReceived(Node node, IncomingMessage message)
		{
			var connection = _Connection;
			if (message.Message.Payload is HeadersPayload h && h.Headers.Count != 0)
			{
				connection.Events.Writer.TryWrite(new PullBlocks(h.Headers));
			}
			else if (message.Message.Payload is BlockPayload b)
			{
				connection.Blocks.Writer.TryWrite(b.Object);
			}
			else if (message.Message.Payload is InvPayload invs)
			{
				if (State != BitcoinDWaiterState.Ready)
					return;
				var data = new GetDataPayload();
				foreach (var inv in invs.Inventory.Where(t => t.Type.HasFlag(InventoryType.MSG_TX)))
				{
					inv.Type = node.AddSupportedOptions(inv.Type);
					data.Inventory.Add(inv);
				}
				if (data.Inventory.Count != 0)
				{
					node.SendMessageAsync(data);
				}
			}
			else if (message.Message.Payload is TxPayload tx)
			{
				connection.Events.Writer.TryWrite(tx.Object);
			}
		}

		private void Node_Disconnected(Node node)
		{
			Logger.LogInformation($"Node disconnected ({node.DisconnectReason.Reason})");
			_Connection?.Dispose();
			node.MessageReceived -= Node_MessageReceived;
			node.Disconnected -= Node_Disconnected;
			State = BitcoinDWaiterState.NotStarted;
		}


		public async Task StartAsync(CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
				return;
			await Task.Yield(); // So it doesn't crash the calling Task.WhenAll
			cts = new CancellationTokenSource();
			_indexerLoop = IndexerLoop();
			_watchdogLoop = WatchdogLoop();
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			cts?.Cancel();
			_Connection?.Dispose("NBXplorer stopping...");
			if (_indexerLoop is not null)
				await _indexerLoop;
			if (_watchdogLoop is not null)
				await _watchdogLoop;
		}
		public NBXplorerNetwork Network => network;

		BitcoinDWaiterState _State = BitcoinDWaiterState.NotStarted;
		public BitcoinDWaiterState State
		{
			get
			{
				return _State;
			}
			set
			{
				if (_State != value)
				{
					var old = _State;
					_State = value;
					EventAggregator.Publish(new BitcoinDStateChangedEvent(Network, old, value));
				}
			}
		}

		public long? SyncHeight => lastIndexedBlock?.Height;

		public GetNetworkInfoResponse NetworkInfo { get; internal set; }
		public AddressPoolService AddressPoolService { get; }
		public ILogger Logger { get; }
		public RPCClient RPCClient { get; }
		public Repository Repository { get; }
		public DbConnectionFactory ConnectionFactory { get; }
		public ExplorerConfiguration ExplorerConfiguration { get; }
		public ChainConfiguration ChainConfiguration { get; }
		public EventAggregator EventAggregator { get; }
		public GetBlockchainInfoResponse BlockchainInfo { get; private set; }

		NBXplorerNetwork network;
		private int maxinflight = 10;

		public async Task SaveMatches(Transaction transaction)
		{
			await using var conn = await ConnectionFactory.CreateConnectionHelper(Network);
			await SaveMatches(conn, new List<Transaction>(1) { transaction }, null, false);
		}

		public RPCClient GetConnectedClient() => State switch
		{
			BitcoinDWaiterState.CoreSynching or BitcoinDWaiterState.NBXplorerSynching or BitcoinDWaiterState.Ready => RPCClient,
			_ => null
		};
	}
}
