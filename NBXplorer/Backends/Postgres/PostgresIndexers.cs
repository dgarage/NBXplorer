using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.RPC;
using NBXplorer.Configuration;
using NBXplorer.Events;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NBXplorer.Backends.Postgres
{
	public class PostgresIndexers : IHostedService, IIndexers
	{
		class PostgresIndexer : IIndexer
		{
			public PostgresIndexer(
				AddressPoolService addressPoolService,
				ILogger logger,
				NBXplorerNetwork network,
				RPCClient rpcClient,
				PostgresRepository repository,
				DbConnectionFactory connectionFactory,
				ChainConfiguration chainConfiguration,
				EventAggregator eventAggregator)
			{
				AddressPoolService = addressPoolService;
				Logger = logger;
				this.network = network;
				RPCClient = rpcClient;
				Repository = repository;
				ConnectionFactory = connectionFactory;
				ChainConfiguration = chainConfiguration;
				EventAggregator = eventAggregator;
			}
			CancellationTokenSource cts;
			Task _indexerLoop;
			Node _Node;
			Channel<object> _Channel = Channel.CreateUnbounded<object>();
			Channel<Block> _DownloadedBlocks = Channel.CreateUnbounded<Block>();
			async Task IndexerLoop()
			{
				TimeSpan retryDelay = TimeSpan.FromSeconds(0);
				retry:
				try
				{
					await IndexerLoopCore(cts.Token);
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

			private async Task IndexerLoopCore(CancellationToken token)
			{
				await ConnectNode(token, true);
				await foreach (var item in _Channel.Reader.ReadAllAsync(token))
				{
					await using var conn = await ConnectionFactory.CreateConnectionHelper(Network, b =>
					{
						b.NoResetOnClose = true;
						// It seems that when running a big rescan, the postgres connection process
						// is taking more and more RAM.
						// While I didn't find the source of the issue, disabling connection pooling
						// will force postgres to create a new connection process, freeing the memory.
						// Note that since PullBlocks are consolidated during rescans, it will only create
						// 1 connection every ~2000 blocks.
						b.Pooling = !(item is PullBlocks && State == BitcoinDWaiterState.NBXplorerSynching);
					});
					if (item is PullBlocks pb)
					{
						var headers = ConsolidatePullBlocks(_Channel.Reader, pb);
						using var pullBlockTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
						pullBlockTimeout.CancelAfter(PullBlockTimeout);
						foreach (var batch in headers.Batch(maxinflight))
						{
							_ = _Node.SendMessageAsync(
								new GetDataPayload(
									batch.Select(b => new InventoryVector(_Node.AddSupportedOptions(InventoryType.MSG_BLOCK), b.GetHash())
									).ToArray()));
							var remaining = batch.Select(b => b.GetHash()).ToHashSet();
							List<Block> unorderedBlocks = new List<Block>();
							await foreach (var block in _DownloadedBlocks.Reader.ReadAllAsync(pullBlockTimeout.Token))
							{
								pullBlockTimeout.CancelAfter(PullBlockTimeout);
								if (!remaining.Remove(block.Header.GetHash()))
									continue;
								if (lastIndexedBlock is null || block.Header.HashPrevBlock == lastIndexedBlock.Hash)
								{
									SlimChainedBlock slimChainedBlock = lastIndexedBlock is null ?
										await RPCClient.GetBlockHeaderAsyncEx(block.Header.GetHash()) :
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
										Task<SlimChainedBlock>[] slimChainedBlocks = new Task<SlimChainedBlock>[unorderedBlocks.Count];
										var rpcBatch = RPCClient.PrepareBatch();
										for (int i = 0; i < unorderedBlocks.Count; i++)
										{
											slimChainedBlocks[i] = rpcBatch.GetBlockHeaderAsyncEx(unorderedBlocks[i].GetHash());
										}
										await rpcBatch.SendBatchAsync();
										// If there is a fork, we should index the unordered blocks
										bool unconfedBlocks = false;
										bool fork = await RPCClient.GetBlockHeaderAsyncEx(lastIndexedBlock.Hash) == null;
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
											await SaveMatches(conn, b.First, slimBlock);
										}
									}
									break;
								}
							}
							await SaveProgress(conn);
							await UpdateState();
						}
						await AskNextHeaders();
					}
					if (item is NodeDisconnected)
					{
						await ConnectNode(token, false);
					}
					if (item is NewTransaction nt)
					{
						await SaveMatches(conn, new List<Transaction>(1) { nt.tx }, null, nt.fireEvents);
					}
				}
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

			private static TimeSpan PullBlockTimeout = TimeSpan.FromMinutes(1.0);

			private async Task ConnectNode(CancellationToken token, bool forceRestart)
			{
				if (_Node is not null)
				{
					if (!forceRestart && _Node.State == NodeState.HandShaked)
						return;
					_Node.DisconnectAsync("Restarting");
					_Node = null;
				}
				State = BitcoinDWaiterState.NotStarted;
				using (var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
				{
					var userAgent = "NBXplorer-" + RandomUtils.GetInt64();
					var node = await Node.ConnectAsync(network.NBitcoinNetwork, ChainConfiguration.NodeEndpoint, new NodeConnectionParameters()
					{
						UserAgent = userAgent,
						ConnectCancellation = handshakeTimeout.Token,
						IsRelay = true
					});
					Logger.LogInformation($"TCP Connection succeed, handshaking...");
					node.VersionHandshake(handshakeTimeout.Token);
					Logger.LogInformation($"Handshaked");
					await node.SendMessageAsync(new SendHeadersPayload());

					await RPCArgs.TestRPCAsync(Network, RPCClient, token, Logger);
					if (await RPCClient.SupportTxIndex() is bool txIndex)
					{
						ChainConfiguration.HasTxIndex = txIndex;
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
						Logger.LogWarning($"{Network.CryptoCode}: Your NBXplorer server is not whitelisted by your node," +
							$" you should add \"whitelist={addressStr}\" to the configuration file of your node. (Or use whitebind)");
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
					_NodeTip = await RPCClient.GetBlockHeaderAsyncEx(BlockchainInfo.BestBlockHash);
					State = BitcoinDWaiterState.NBXplorerSynching;
					// Refresh the NetworkInfo that may have become different while it was synching.
					NetworkInfo = await RPCClient.GetNetworkInfoAsync();
					_Node = node;
					EmptyChannel(_Channel);
					EmptyChannel(_DownloadedBlocks);
					node.MessageReceived += Node_MessageReceived;
					node.Disconnected += Node_Disconnected;

					var locator = await AskNextHeaders();
					lastIndexedBlock = await Repository.GetLastIndexedSlimChainedBlock(locator);
					if (lastIndexedBlock is null)
					{
						var locatorTip = await RPCClient.GetBlockHeaderAsyncEx(locator.Blocks[0]);
						lastIndexedBlock = locatorTip;
					}
					await UpdateState();
				}
			}

			private void EmptyChannel<T>(Channel<T> channel)
			{
				while (channel.Reader.TryRead(out _)) { }
			}

			bool firstConnect = true;
			private async Task<BlockLocator> AskNextHeaders()
			{
				var indexProgress = await Repository.GetIndexProgress();
				if (indexProgress is null)
				{
					indexProgress = await GetDefaultCurrentLocation();
				}
				await _Node.SendMessageAsync(new GetHeadersPayload(indexProgress));
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

			private async Task UpdateState()
			{
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

			private async Task<BlockLocator> GetDefaultCurrentLocation()
			{
				if (ChainConfiguration.StartHeight > BlockchainInfo.Headers)
					throw new InvalidOperationException($"{Network.CryptoCode}: StartHeight should not be above the current tip");
				BlockLocator blockLocator = null;
				if (ChainConfiguration.StartHeight == -1)
				{
					var bestBlock = await RPCClient.GetBestBlockHashAsync();
					var bh = await RPCClient.GetBlockHeaderAsyncEx(bestBlock);
					blockLocator = new BlockLocator();
					blockLocator.Blocks.Add(bh.Previous ?? bh.Hash);
					Logger.LogInformation($"Current Index Progress not found, start syncing from the header's chain tip (At height: {BlockchainInfo.Headers})");
				}
				else
				{
					var header = await RPCClient.GetBlockHeaderAsync(ChainConfiguration.StartHeight);
					var header2 = await RPCClient.GetBlockHeaderAsyncEx(header.GetHash());
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
							Outputs = matches[i].GetReceivedOutputs().ToList()
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
			record NewTransaction(Transaction tx, bool fireEvents);
			record NodeDisconnected();
			private void Node_MessageReceived(Node node, IncomingMessage message)
			{
				if (message.Message.Payload is HeadersPayload h && h.Headers.Count != 0)
				{
					_Channel.Writer.TryWrite(new PullBlocks(h.Headers));
				}
				else if (message.Message.Payload is BlockPayload b)
				{
					_DownloadedBlocks.Writer.TryWrite(b.Object);
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
						node.SendMessageAsync(data);
				}
				else if (message.Message.Payload is TxPayload tx)
				{
					_Channel.Writer.TryWrite(new NewTransaction(tx.Object, true));
				}
			}

			private void Node_Disconnected(Node node)
			{
				if (node.DisconnectReason.Reason != "Restarting")
				{
					if (!cts.IsCancellationRequested)
					{
						var exception = node.DisconnectReason.Exception?.Message;
						if (!string.IsNullOrEmpty(exception))
							exception = $" ({exception})";
						else
							exception = String.Empty;
						Logger.LogWarning($"Node disconnected for reason: {node.DisconnectReason.Reason}{exception}");
					}
					_Channel.Writer.TryWrite(new NodeDisconnected());
				}
				else
				{
					Logger.LogInformation($"Restarting node connection...");
				}
				node.MessageReceived -= Node_MessageReceived;
				node.Disconnected -= Node_Disconnected;
				State = BitcoinDWaiterState.NotStarted;
			}


			public Task StartAsync(CancellationToken cancellationToken)
			{
				if (cancellationToken.IsCancellationRequested)
					return Task.CompletedTask;
				cts = new CancellationTokenSource();
				_indexerLoop = IndexerLoop();
				return Task.CompletedTask;
			}
			public async Task StopAsync(CancellationToken cancellationToken)
			{
				cts?.Cancel();
				_Channel.Writer.Complete();
				if (_indexerLoop is not null)
					await _indexerLoop;
				_Node?.DisconnectAsync();
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
			public PostgresRepository Repository { get; }
			public DbConnectionFactory ConnectionFactory { get; }
			public ChainConfiguration ChainConfiguration { get; }
			public EventAggregator EventAggregator { get; }
			public GetBlockchainInfoResponse BlockchainInfo { get; private set; }

			NBXplorerNetwork network;
			private int maxinflight = 10;

			public Task SaveMatches(Transaction transaction)
			{
				return SaveMatches(transaction, false);
			}
			public async Task SaveMatches(Transaction transaction, bool fireEvents)
			{
				await using var conn = await ConnectionFactory.CreateConnectionHelper(Network);
				await SaveMatches(conn, new List<Transaction>(1) { transaction }, null, fireEvents);
			}

			public RPCClient GetConnectedClient()
			{
				if (State == BitcoinDWaiterState.CoreSynching || State == BitcoinDWaiterState.NBXplorerSynching || State == BitcoinDWaiterState.Ready)
					return RPCClient;
				return null;
			}
		}

		Dictionary<string, IIndexer> _Indexers = new Dictionary<string, IIndexer>();

		public AddressPoolService AddressPoolService { get; }
		public ILoggerFactory LoggerFactory { get; }
		public IRPCClients RpcClients { get; }
		public ExplorerConfiguration Configuration { get; }
		public NBXplorerNetworkProvider NetworkProvider { get; }
		public IRepositoryProvider RepositoryProvider { get; }
		public DbConnectionFactory ConnectionFactory { get; }
		public EventAggregator EventAggregator { get; }

		public PostgresIndexers(
			AddressPoolService addressPoolService,
			ILoggerFactory loggerFactory,
			IRPCClients rpcClients,
			ExplorerConfiguration configuration,
			NBXplorerNetworkProvider networkProvider,
			IRepositoryProvider repositoryProvider,
			DbConnectionFactory connectionFactory,
			EventAggregator eventAggregator)
		{
			AddressPoolService = addressPoolService;
			LoggerFactory = loggerFactory;
			RpcClients = rpcClients;
			Configuration = configuration;
			NetworkProvider = networkProvider;
			RepositoryProvider = repositoryProvider;
			ConnectionFactory = connectionFactory;
			EventAggregator = eventAggregator;
		}
		public async Task StartAsync(CancellationToken cancellationToken)
		{
			foreach (var config in Configuration.ChainConfigurations)
			{
				var network = NetworkProvider.GetFromCryptoCode(config.CryptoCode);
				_Indexers.Add(config.CryptoCode, new PostgresIndexer(
					AddressPoolService,
					LoggerFactory.CreateLogger($"NBXplorer.Indexer.{config.CryptoCode}"),
					network,
					RpcClients.Get(network),
					(PostgresRepository)RepositoryProvider.GetRepository(network),
					ConnectionFactory,
					config,
					EventAggregator));
			}
			await Task.WhenAll(_Indexers.Values.Select(v => ((PostgresIndexer)v).StartAsync(cancellationToken)));
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			await Task.WhenAll(_Indexers.Values.Select(v => ((PostgresIndexer)v).StopAsync(cancellationToken)));
		}

		public IIndexer GetIndexer(NBXplorerNetwork network)
		{
			_Indexers.TryGetValue(network.CryptoCode, out var r);
			return r;
		}

		public IEnumerable<IIndexer> All()
		{
			return _Indexers.Values;
		}
	}
}
