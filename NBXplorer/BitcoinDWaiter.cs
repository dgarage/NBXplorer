using NBitcoin.RPC;
using Microsoft.Extensions.Logging;
using NBXplorer.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBXplorer.Configuration;
using NBitcoin.Protocol;
using System.Threading;
using System.IO;
using NBitcoin;
using System.Net;
using NBitcoin.Protocol.Behaviors;
using Microsoft.Extensions.Hosting;
using NBXplorer.Events;
using Newtonsoft.Json.Linq;
using System.Text;

namespace NBXplorer
{
	public enum BitcoinDWaiterState
	{
		NotStarted,
		CoreSynching,
		NBXplorerSynching,
		Ready
	}

	public class BitcoinDWaiters : IHostedService
	{
		Dictionary<string, BitcoinDWaiter> _Waiters;
		private readonly RepositoryProvider repositoryProvider;

		public BitcoinDWaiters(
							AddressPoolServiceAccessor addressPool,
								NBXplorerNetworkProvider networkProvider,
							  ChainProvider chains,
							  RepositoryProvider repositoryProvider,
							  ExplorerConfiguration config,
							  RPCClientProvider rpcProvider,
							  EventAggregator eventAggregator,
							  KeyPathTemplates keyPathTemplates)
		{
			_Waiters = networkProvider
				.GetAll()
				.Select(s => (Repository: repositoryProvider.GetRepository(s),
							  RPCClient: rpcProvider.GetRPCClient(s),
							  Chain: chains.GetChain(s),
							  Network: s))
				.Where(s => s.Repository != null && s.RPCClient != null && s.Chain != null)
				.Select(s => new BitcoinDWaiter(s.RPCClient,
												config,
												networkProvider.GetFromCryptoCode(s.Network.CryptoCode),
												s.Chain,
												s.Repository,
												addressPool.Instance,
												eventAggregator, keyPathTemplates))
				.ToDictionary(s => s.Network.CryptoCode, s => s);
			this.repositoryProvider = repositoryProvider;
		}
		public async Task StartAsync(CancellationToken cancellationToken)
		{
			await repositoryProvider.StartAsync();
			await Task.WhenAll(_Waiters.Select(s => s.Value.StartAsync(cancellationToken)).ToArray());
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			await Task.WhenAll(_Waiters.Select(s => s.Value.StopAsync(cancellationToken)).ToArray());
			await repositoryProvider.DisposeAsync();
		}

		public BitcoinDWaiter GetWaiter(NBXplorerNetwork network)
		{
			return GetWaiter(network.CryptoCode);
		}
		public BitcoinDWaiter GetWaiter(string cryptoCode)
		{
			_Waiters.TryGetValue(cryptoCode.ToUpperInvariant(), out BitcoinDWaiter waiter);
			return waiter;
		}

		public IEnumerable<BitcoinDWaiter> All()
		{
			return _Waiters.Values;
		}
	}

	public class BitcoinDWaiter : IHostedService
	{
		RPCClient _RPCWithTimeout;
		RPCClient _OriginalRPC;
		NBXplorerNetwork _Network;
		ExplorerConfiguration _Configuration;
		private ExplorerBehavior _ExplorerPrototype;
		SlimChain _Chain;
		EventAggregator _EventAggregator;
		private readonly ChainConfiguration _ChainConfiguration;
		readonly string RPCReadyFile;

		public BitcoinDWaiter(
			RPCClient rpc,
			ExplorerConfiguration configuration,
			NBXplorerNetwork network,
			SlimChain chain,
			Repository repository,
			AddressPoolService addressPoolService,
			EventAggregator eventAggregator,
			KeyPathTemplates keyPathTemplates)
		{
			if (addressPoolService == null)
				throw new ArgumentNullException(nameof(addressPoolService));
			_OriginalRPC = rpc;
			_RPCWithTimeout = rpc.Clone();
			_RPCWithTimeout.RequestTimeout = TimeSpan.FromMinutes(1.0);
			_Configuration = configuration;
			_Network = network;
			_Chain = chain;
			State = BitcoinDWaiterState.NotStarted;
			_EventAggregator = eventAggregator;
			_ChainConfiguration = _Configuration.ChainConfigurations.First(c => c.CryptoCode == _Network.CryptoCode);
			_ExplorerPrototype = new ExplorerBehavior(repository, chain, addressPoolService, eventAggregator, keyPathTemplates) { StartHeight = _ChainConfiguration.StartHeight };
			RPCReadyFile = Path.Combine(configuration.SignalFilesDir, $"{network.CryptoCode.ToLowerInvariant()}_fully_synched");
		}
		public NodeState NodeState
		{
			get;
			private set;
		}

		private Node _Node;


		public NBXplorerNetwork Network
		{
			get
			{
				return _Network;
			}
		}

		public RPCClient RPC
		{
			get
			{
				return _RPCWithTimeout;
			}
		}

		public BitcoinDWaiterState State
		{
			get;
			private set;
		}



		public bool RPCAvailable
		{
			get
			{
				return State == BitcoinDWaiterState.Ready ||
					State == BitcoinDWaiterState.CoreSynching ||
					State == BitcoinDWaiterState.NBXplorerSynching;
			}
		}
		IDisposable _Subscription;
		Task _Loop;
		CancellationTokenSource _Cts;
		public Task StartAsync(CancellationToken cancellationToken)
		{
			if (_Disposed)
				throw new ObjectDisposedException(nameof(BitcoinDWaiter));

			_Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			_Loop = StartLoop(_Cts.Token, _Tick);
			_Subscription = _EventAggregator.Subscribe<Models.NewBlockEvent>(s =>
			{
				_Tick.Set();
			});
			return Task.CompletedTask;
		}

		Signaler _Tick = new Signaler();

		private async Task StartLoop(CancellationToken token, Signaler tick)
		{
			try
			{
				int errors = 0;
				while (!token.IsCancellationRequested)
				{
					errors = Math.Min(11, errors);
					try
					{
						while (await StepAsync(token))
						{
						}
						await tick.Wait(PollingInterval, token);
						errors = 0;
					}
					catch (ConfigException) when (!token.IsCancellationRequested)
					{
						// Probably RPC errors, don't spam
						await Wait(errors, tick, token);
						errors++;
					}
					catch (Exception ex) when (!token.IsCancellationRequested)
					{
						Logs.Configuration.LogError(ex, $"{_Network.CryptoCode}: Unhandled in Waiter loop");
						await Wait(errors, tick, token);
						errors++;
					}
				}
			}
			catch when (token.IsCancellationRequested)
			{
			}
		}

		private async Task Wait(int errors, Signaler tick, CancellationToken token)
		{
			var timeToWait = TimeSpan.FromSeconds(5.0) * (errors + 1);
			Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Testing again in {(int)timeToWait.TotalSeconds} seconds");
			await tick.Wait(timeToWait, token);
		}

		public BlockLocator GetLocation()
		{
			return GetExplorerBehavior()?.CurrentLocation;
		}

		public TimeSpan PollingInterval
		{
			get; set;
		} = TimeSpan.FromMinutes(1.0);

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			_Disposed = true;
			_Cts.Cancel();
			_Subscription.Dispose();
			EnsureNodeDisposed();
			State = BitcoinDWaiterState.NotStarted;
			_Chain = null;
			try
			{
				await _Loop;
			}
			catch { }
			EnsureRPCReadyFileDeleted();
		}
		bool _BanListLoaded;
		async Task<bool> StepAsync(CancellationToken token)
		{
			var oldState = State;
			switch (State)
			{
				case BitcoinDWaiterState.NotStarted:
					await RPCArgs.TestRPCAsync(_Network, _RPCWithTimeout, token);
					_OriginalRPC.Capabilities = _RPCWithTimeout.Capabilities;
					GetBlockchainInfoResponse blockchainInfo = null;
					try
					{
						blockchainInfo = await _RPCWithTimeout.GetBlockchainInfoAsyncEx();
						if (_Network.CryptoCode == "BTC" &&
							_Network.NBitcoinNetwork.NetworkType == NetworkType.Mainnet &&
							!_BanListLoaded)
						{
							if (await LoadBanList())
								_BanListLoaded = true;
						}
						if(blockchainInfo != null && _Network.NBitcoinNetwork.NetworkType == NetworkType.Regtest && !_ChainConfiguration.NoWarmup)
						{
							if (await WarmupBlockchain())
							{
								blockchainInfo = await _RPCWithTimeout.GetBlockchainInfoAsyncEx();
							}
						}
					}
					catch (Exception ex)
					{
						Logs.Configuration.LogError(ex, $"{_Network.CryptoCode}: Failed to connect to RPC");
						break;
					}
					if (IsSynchingCore(blockchainInfo))
					{
						State = BitcoinDWaiterState.CoreSynching;
					}
					else
					{
						await ConnectToBitcoinD(token);
						State = BitcoinDWaiterState.NBXplorerSynching;
					}
					break;
				case BitcoinDWaiterState.CoreSynching:
					GetBlockchainInfoResponse blockchainInfo2 = null;
					try
					{
						blockchainInfo2 = await _RPCWithTimeout.GetBlockchainInfoAsyncEx();
					}
					catch (Exception ex)
					{
						Logs.Configuration.LogError(ex, $"{_Network.CryptoCode}: Failed to connect to RPC");
						State = BitcoinDWaiterState.NotStarted;
						break;
					}
					if (!IsSynchingCore(blockchainInfo2))
					{
						await ConnectToBitcoinD(token);
						State = BitcoinDWaiterState.NBXplorerSynching;
					}
					break;
				case BitcoinDWaiterState.NBXplorerSynching:
					var explorer = GetExplorerBehavior();
					if (explorer == null)
					{
						State = BitcoinDWaiterState.NotStarted;
					}
					else if (!explorer.IsSynching())
					{
						State = BitcoinDWaiterState.Ready;
					}
					break;
				case BitcoinDWaiterState.Ready:
					var explorer2 = GetExplorerBehavior();
					if (explorer2 == null)
					{
						State = BitcoinDWaiterState.NotStarted;
					}
					else if (explorer2.IsSynching())
					{
						State = BitcoinDWaiterState.NBXplorerSynching;
					}
					break;
				default:
					break;
			}
			var changed = oldState != State;

			if (changed)
			{
				if (oldState == BitcoinDWaiterState.NotStarted)
					NetworkInfo = await _RPCWithTimeout.GetNetworkInfoAsync();
				_EventAggregator.Publish(new BitcoinDStateChangedEvent(_Network, oldState, State));
				if (State == BitcoinDWaiterState.Ready)
				{
					await File.WriteAllTextAsync(RPCReadyFile, NBitcoin.Utils.DateTimeToUnixTime(DateTimeOffset.UtcNow).ToString());
				}
			}
			if (State != BitcoinDWaiterState.Ready)
			{
				EnsureRPCReadyFileDeleted();
			}
			return changed;
		}

		private Node GetHandshakedNode()
		{
			return _Node?.State == NodeState.HandShaked ? _Node : null;
		}

		private ExplorerBehavior GetExplorerBehavior()
		{
			return GetHandshakedNode()?.Behaviors?.Find<ExplorerBehavior>();
		}

		private void EnsureRPCReadyFileDeleted()
		{
			if (File.Exists(RPCReadyFile))
				File.Delete(RPCReadyFile);
		}

		private async Task<bool> LoadBanList()
		{
			var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("NBXplorer.banlist.cli.txt");
			string content = null;
			using (var reader = new StreamReader(stream, Encoding.UTF8))
			{
				content = reader.ReadToEnd();
			}
			var bannedLines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
			var batch = _RPCWithTimeout.PrepareBatch();
			var commands = bannedLines
						.Where(o => o.Length > 0 && o[0] != '#')
						.Select(b => b.Split(' ')[2])
						.Select(ip => batch.SendCommandAsync(new RPCRequest("setban", new object[] { ip, "add", 31557600 }), false))
						.ToArray();
			await batch.SendBatchAsync();
			foreach (var command in commands)
			{
				var result = await command;
				if (result.Error != null && result.Error.Code != RPCErrorCode.RPC_CLIENT_NODE_ALREADY_ADDED)
					result.ThrowIfError();
			}
			Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Node banlist loaded");
			return true;
		}

		private async Task ConnectToBitcoinD(CancellationToken cancellation)
		{
			var node = GetHandshakedNode();
			if (node != null)
				return;
			try
			{
				EnsureNodeDisposed();
				_Chain.ResetToGenesis();
				if (_Configuration.CacheChain)
				{
					LoadChainFromCache();
					if (!await HasBlock(_RPCWithTimeout, _Chain.Tip))
					{
						Logs.Configuration.LogInformation($"{_Network.CryptoCode}: The cached chain contains a tip unknown to the node, dropping the cache...");
						_Chain.ResetToGenesis();
					}
				}
				var heightBefore = _Chain.Height;
				using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
				{
					timeout.CancelAfter(_Network.ChainLoadingTimeout);
					try
					{
						Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Trying to connect via the P2P protocol to trusted node ({_ChainConfiguration.NodeEndpoint.ToEndpointString()})...");
						var userAgent = "NBXplorer-" + RandomUtils.GetInt64();
						bool handshaked = false;
						using (var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
						{
							try
							{
								handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(10));
								node = await Node.ConnectAsync(_Network.NBitcoinNetwork, _ChainConfiguration.NodeEndpoint, new NodeConnectionParameters()
								{
									UserAgent = userAgent,
									ConnectCancellation = handshakeTimeout.Token,
									IsRelay = true
								});

								try
								{
									Logs.Explorer.LogInformation($"{Network.CryptoCode}: TCP Connection succeed, handshaking...");
									node.VersionHandshake(handshakeTimeout.Token);
									Logs.Explorer.LogInformation($"{Network.CryptoCode}: Handshaked");
								}
								catch (OperationCanceledException) when (handshakeTimeout.IsCancellationRequested)
								{
									Logs.Explorer.LogWarning($"{Network.CryptoCode}: NBXplorer could not complete the handshake with the remote node. This is probably because NBXplorer is not whitelisted by your node.{Environment.NewLine}" +
										$"You can use \"whitebind\" or \"whitelist\" in your node configuration. (typically whitelist=127.0.0.1 if NBXplorer and the node are on the same machine.){Environment.NewLine}" +
										$"This issue can also happen because NBXplorer do not manage to connect to the P2P port of your node at all.");
									throw;
								}
								handshaked = true;
								var loadChainTimeout = _Network.NBitcoinNetwork.NetworkType == NetworkType.Regtest ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(15);
								if (_Chain.Height < 5)
									loadChainTimeout = TimeSpan.FromDays(7); // unlimited
								Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Loading chain from node");
								try
								{
									using (var cts1 = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
									{
										cts1.CancelAfter(loadChainTimeout);
										Logs.Explorer.LogInformation($"{Network.CryptoCode}: Loading chain...");
										node.SynchronizeSlimChain(_Chain, cancellationToken: cts1.Token);
									}
								}
								catch when (!cancellation.IsCancellationRequested) // Timeout happens with SynchronizeChain, if so, throw away the cached chain
								{
									Logs.Explorer.LogInformation($"{Network.CryptoCode}: Failed to load chain before timeout, let's try again without the chain cache...");
									_Chain.ResetToGenesis();
									node.SynchronizeSlimChain(_Chain, cancellationToken: cancellation);
								}
								Logs.Explorer.LogInformation($"{Network.CryptoCode}: Chain loaded");

								var peer = (await _RPCWithTimeout.GetPeersInfoAsync())
											.FirstOrDefault(p => p.SubVersion == userAgent);
								if (peer != null && !peer.IsWhiteListed)
								{
									var addressStr = peer.Address?.Address?.ToString();
									if (addressStr == null)
									{
										addressStr = peer.AddressString;
										var portDelimiter = addressStr.LastIndexOf(':');
										if (portDelimiter != -1)
											addressStr = addressStr.Substring(0, portDelimiter);
									}

									Logs.Explorer.LogWarning($"{Network.CryptoCode}: Your NBXplorer server is not whitelisted by your node," +
										$" you should add \"whitelist={addressStr}\" to the configuration file of your node. (Or use whitebind)");
								}
								if (peer != null && peer.IsWhiteListed)
								{
									Logs.Explorer.LogInformation($"{Network.CryptoCode}: NBXplorer is correctly whitelisted by the node");
								}
							}
							catch (OperationCanceledException) when (!handshaked && handshakeTimeout.IsCancellationRequested)
							{
								Logs.Explorer.LogWarning($"{Network.CryptoCode}: The initial hanshake failed, your NBXplorer server might not be whitelisted by your node," +
										$" if your bitcoin node is on the same machine as NBXplorer, you should add \"whitelist=127.0.0.1\" to the configuration file of your node. (Or use whitebind)");
								throw;
							}
						}
						Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Height: " + _Chain.Height);
					}
					catch (Exception ex) when (!cancellation.IsCancellationRequested)
					{
						throw new OperationCanceledException("Loading the chain from the node timed out", ex, timeout.Token);
					}
				}
				if (_Configuration.CacheChain && heightBefore != _Chain.Height)
				{
					SaveChainInCache();
				}
				GC.Collect();
				node.Behaviors.Add(new SlimChainBehavior(_Chain));
				var explorer = (ExplorerBehavior)_ExplorerPrototype.Clone();
				node.Behaviors.Add(explorer);
				node.StateChanged += Node_StateChanged;
				_Node = node;
				await explorer.Init();
			}
			catch
			{
				EnsureNodeDisposed(node ?? _Node);
				throw;
			}
		}

		private void Node_StateChanged(Node node, NodeState oldState)
		{
			_Tick.Set();
		}

		private void EnsureNodeDisposed(Node node = null)
		{
			node = node ?? _Node;
			if (node != null)
			{
				try
				{
					node.StateChanged -= Node_StateChanged;
					node.DisconnectAsync();
				}
				catch { }
				node = null;
				_Node = null;
			}
		}

		private async Task<bool> HasBlock(RPCClient rpc, uint256 tip)
		{
			try
			{
				await rpc.GetBlockHeaderAsync(tip);
				return true;
			}
			catch (RPCException r) when (r.RPCCode == RPCErrorCode.RPC_METHOD_NOT_FOUND)
			{
				try
				{
					await rpc.GetBlockAsync(tip);
					return true;
				}
				catch
				{
					return false;
				}
			}
			catch (RPCException r) when (r.RPCCode == RPCErrorCode.RPC_INVALID_ADDRESS_OR_KEY)
			{
				return false;
			}
		}

		private void SaveChainInCache()
		{
			var suffix = _Network.CryptoCode == "BTC" ? "" : _Network.CryptoCode;
			var cachePath = Path.Combine(_Configuration.DataDir, $"{suffix}chain-slim.dat");
			var cachePathTemp = Path.Combine(_Configuration.DataDir, $"{suffix}chain-slim.dat.temp");

			Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Saving chain to cache...");
			using (var fs = new FileStream(cachePathTemp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024))
			{
				_Chain.Save(fs);
				fs.Flush();
			}

			if (File.Exists(cachePath))
				File.Delete(cachePath);
			File.Move(cachePathTemp, cachePath);
			Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Chain cached");
		}

		private void LoadChainFromCache()
		{
			var suffix = _Network.CryptoCode == "BTC" ? "" : _Network.CryptoCode;
			{
				var legacyCachePath = Path.Combine(_Configuration.DataDir, $"{suffix}chain.dat");
				if (_Configuration.CacheChain && File.Exists(legacyCachePath))
				{
					Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Loading chain from cache...");
					var chain = new ConcurrentChain(_Network.NBitcoinNetwork);
					chain.Load(File.ReadAllBytes(legacyCachePath), _Network.NBitcoinNetwork);
					LoadSlimAndSaveToSlimFormat(chain);
					File.Delete(legacyCachePath);
					Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Height: " + _Chain.Height);
					return;
				}
			}

			{
				var cachePath = Path.Combine(_Configuration.DataDir, $"{suffix}chain-stripped.dat");
				if (_Configuration.CacheChain && File.Exists(cachePath))
				{
					Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Loading chain from cache...");
					var chain = new ConcurrentChain(_Network.NBitcoinNetwork);
					chain.Load(File.ReadAllBytes(cachePath), _Network.NBitcoinNetwork, new ConcurrentChain.ChainSerializationFormat()
					{
						SerializeBlockHeader = false,
						SerializePrecomputedBlockHash = true,
					});
					LoadSlimAndSaveToSlimFormat(chain);
					File.Delete(cachePath);
					Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Height: " + _Chain.Height);
					return;
				}
			}

			{
				var slimCachePath = Path.Combine(_Configuration.DataDir, $"{suffix}chain-slim.dat");
				if (_Configuration.CacheChain && File.Exists(slimCachePath))
				{
					Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Loading chain from cache...");
					using (var file = new FileStream(slimCachePath, FileMode.Open, FileAccess.Read, FileShare.None, 1024 * 1024))
					{
						_Chain.Load(file);
					}
					Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Height: " + _Chain.Height);
					return;
				}
			}
		}

		private void LoadSlimAndSaveToSlimFormat(ConcurrentChain chain)
		{
			foreach (var block in chain.ToEnumerable(false))
			{
				_Chain.TrySetTip(block.HashBlock, block.Previous?.HashBlock);
			}
			SaveChainInCache();
		}

		private async Task<bool> WarmupBlockchain()
		{
			if (await _RPCWithTimeout.GetBlockCountAsync() < _Network.NBitcoinNetwork.Consensus.CoinbaseMaturity)
			{
				Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Less than {_Network.NBitcoinNetwork.Consensus.CoinbaseMaturity} blocks, mining some block for regtest");
				await _RPCWithTimeout.EnsureGenerateAsync(_Network.NBitcoinNetwork.Consensus.CoinbaseMaturity + 1);
				return true;
			}
			else
			{
				var hash = await _RPCWithTimeout.GetBestBlockHashAsync();

				BlockHeader header = null;
				try
				{
					header = await _RPCWithTimeout.GetBlockHeaderAsync(hash);
				}
				catch (RPCException ex) when (ex.RPCCode == RPCErrorCode.RPC_METHOD_NOT_FOUND)
				{
					header = (await _RPCWithTimeout.GetBlockAsync(hash)).Header;
				}
				if ((DateTimeOffset.UtcNow - header.BlockTime) > TimeSpan.FromSeconds(24 * 60 * 60))
				{
					Logs.Configuration.LogInformation($"{_Network.CryptoCode}: It has been a while nothing got mined on regtest... mining 10 blocks");
					await _RPCWithTimeout.GenerateAsync(10);
					return true;
				}
				return false;
			}
		}

		public bool IsSynchingCore(GetBlockchainInfoResponse blockchainInfo)
		{
			if (blockchainInfo.InitialBlockDownload == true)
				return true;
			if (blockchainInfo.MedianTime.HasValue && _Network.NBitcoinNetwork.NetworkType != NetworkType.Regtest)
			{
				var time = NBitcoin.Utils.UnixTimeToDateTime(blockchainInfo.MedianTime.Value);
				// 5 month diff? probably synching...
				if (DateTimeOffset.UtcNow - time > TimeSpan.FromDays(30 * 5))
				{
					return true;
				}
			}

			return blockchainInfo.Headers - blockchainInfo.Blocks > 6;
		}

		bool _Disposed = false;

		public bool Connected
		{
			get
			{
				return GetHandshakedNode() != null;
			}
		}

		public GetNetworkInfoResponse NetworkInfo { get; internal set; }
	}
}
