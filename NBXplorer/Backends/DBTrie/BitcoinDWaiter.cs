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

namespace NBXplorer.Backends.DBTrie
{
	public class BitcoinDWaiters : IHostedService, IRPCClients, IIndexers
	{
		Dictionary<string, BitcoinDWaiter> _Waiters;
		private readonly AddressPoolService addressPool;
		private readonly NBXplorerNetworkProvider networkProvider;
		private readonly ChainProvider chains;
		private readonly IRepositoryProvider repositoryProvider;
		private readonly ExplorerConfiguration config;
		private readonly IRPCClients rpcProvider;
		private readonly EventAggregator eventAggregator;

		public ILoggerFactory LoggerFactory { get; }

		public BitcoinDWaiters(
							ILoggerFactory loggerFactory,
							AddressPoolService addressPool,
							  NBXplorerNetworkProvider networkProvider,
							  ChainProvider chains,
							  IRepositoryProvider repositoryProvider,
							  ExplorerConfiguration config,
							  IRPCClients rpcProvider,
							  EventAggregator eventAggregator)
		{
			LoggerFactory = loggerFactory;
			this.addressPool = addressPool;
			this.networkProvider = networkProvider;
			this.chains = chains;
			this.repositoryProvider = repositoryProvider;
			this.config = config;
			this.rpcProvider = rpcProvider;
			this.eventAggregator = eventAggregator;
		}
		public async Task StartAsync(CancellationToken cancellationToken)
		{
			await repositoryProvider.StartCompletion;
			_Waiters = networkProvider
				.GetAll()
				.Select(s => (Repository: (Repository)repositoryProvider.GetRepository(s),
							  RPCClient: rpcProvider.Get(s),
							  Chain: chains.GetChain(s),
							  Network: s))
				.Where(s => s.Repository != null && s.RPCClient != null && s.Chain != null)
				.Select(s => new BitcoinDWaiter(
												LoggerFactory.CreateLogger($"NBXplorer.BitcoinDWaiters.{s.Network.CryptoCode}"),
												s.RPCClient,
												config,
												networkProvider.GetFromCryptoCode(s.Network.CryptoCode),
												s.Chain,
												s.Repository,
												addressPool,
												eventAggregator))
				.ToDictionary(s => s.Network.CryptoCode, s => s);
			await Task.WhenAll(_Waiters.Select(s => s.Value.StartAsync(cancellationToken)).ToArray());
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			await Task.WhenAll(_Waiters.Select(s => s.Value.StopAsync(cancellationToken)).ToArray());
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

		public RPCClient Get(NBXplorerNetwork network)
		{
			return GetWaiter(network)?.RPC;
		}

		public RPCClient GetAvailableRPCClient(NBXplorerNetwork network)
		{
			var waiter = GetWaiter(network);
			if (!waiter.RPCAvailable)
				return null;
			return waiter.RPC;
		}

		public IIndexer GetIndexer(NBXplorerNetwork network)
		{
			return GetWaiter(network);
		}

		IEnumerable<IIndexer> IIndexers.All()
		{
			return All();
		}
	}

	public class BitcoinDWaiter : IHostedService, IIndexer
	{
		RPCClient _OriginalRPC;
		NBXplorerNetwork _Network;
		ExplorerConfiguration _Configuration;
		private ExplorerBehavior _ExplorerPrototype;
		SlimChain _Chain;
		EventAggregator _EventAggregator;
		private readonly ChainConfiguration _ChainConfiguration;

		public BitcoinDWaiter(
			ILogger logger,
			RPCClient rpc,
			ExplorerConfiguration configuration,
			NBXplorerNetwork network,
			SlimChain chain,
			Repository repository,
			AddressPoolService addressPoolService,
			EventAggregator eventAggregator)
		{
			if (addressPoolService == null)
				throw new ArgumentNullException(nameof(addressPoolService));
			Logger = logger;
			_OriginalRPC = rpc;
			_Configuration = configuration;
			_Network = network;
			_Chain = chain;
			State = BitcoinDWaiterState.NotStarted;
			_EventAggregator = eventAggregator;
			_ChainConfiguration = _Configuration.ChainConfigurations.First(c => c.CryptoCode == _Network.CryptoCode);
			_ExplorerPrototype = new ExplorerBehavior(repository, chain, addressPoolService, eventAggregator) { StartHeight = _ChainConfiguration.StartHeight };
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
				return _OriginalRPC;
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
			_Subscription = _EventAggregator.Subscribe<FullySynchedEvent>(s =>
			{
				if (s.Network == Network)
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
			finally
			{
				EnsureNodeDisposed();
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
		}
		async Task<bool> StepAsync(CancellationToken token)
		{
			var oldState = State;
			switch (State)
			{
				case BitcoinDWaiterState.NotStarted:
					await RPCArgs.TestRPCAsync(_Network, _OriginalRPC, token, Logger);
					_OriginalRPC.Capabilities = _OriginalRPC.Capabilities;
					GetBlockchainInfoResponse blockchainInfo = null;
					try
					{
						blockchainInfo = await _OriginalRPC.GetBlockchainInfoAsyncEx();
						if (blockchainInfo != null && _Network.NBitcoinNetwork.ChainName == ChainName.Regtest && !_ChainConfiguration.NoWarmup)
						{
							if (await _OriginalRPC.WarmupBlockchain(Logs.Explorer))
							{
								blockchainInfo = await _OriginalRPC.GetBlockchainInfoAsyncEx();
							}
						}
					}
					catch (Exception ex)
					{
						Logs.Configuration.LogError(ex, $"{_Network.CryptoCode}: Failed to connect to RPC");
						break;
					}
					if (blockchainInfo.IsSynching(_Network))
					{
						State = BitcoinDWaiterState.CoreSynching;
					}
					else
					{
						await ConnectToBitcoinD(token, blockchainInfo);
						State = BitcoinDWaiterState.NBXplorerSynching;
					}
					break;
				case BitcoinDWaiterState.CoreSynching:
					GetBlockchainInfoResponse blockchainInfo2 = null;
					try
					{
						blockchainInfo2 = await _OriginalRPC.GetBlockchainInfoAsyncEx();
					}
					catch (Exception ex)
					{
						Logs.Configuration.LogError(ex, $"{_Network.CryptoCode}: Failed to connect to RPC");
						State = BitcoinDWaiterState.NotStarted;
						break;
					}
					if (!blockchainInfo2.IsSynching(_Network))
					{
						await ConnectToBitcoinD(token, blockchainInfo2);
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
					NetworkInfo = await _OriginalRPC.GetNetworkInfoAsync();
				_EventAggregator.Publish(new BitcoinDStateChangedEvent(_Network, oldState, State));
			}
			return changed;
		}

		private Node GetHandshakedNode()
		{
			return _Node?.State == NodeState.HandShaked ? _Node : null;
		}

		internal ExplorerBehavior GetExplorerBehavior()
		{
			return GetHandshakedNode()?.Behaviors?.Find<ExplorerBehavior>();
		}

		private async Task ConnectToBitcoinD(CancellationToken cancellation, GetBlockchainInfoResponse blockchainInfo)
		{
			var node = GetHandshakedNode();
			if (node != null)
				return;
			try
			{
				EnsureNodeDisposed();
				_Chain.ResetToGenesis();
				_Chain.SetCapacity((int)(blockchainInfo.Headers * 1.1));
				if (_Configuration.CacheChain)
				{
					LoadChainFromCache();
					if (!await HasBlock(_OriginalRPC, _Chain.Tip))
					{
						Logs.Configuration.LogInformation($"{_Network.CryptoCode}: The cached chain contains a tip unknown to the node, dropping the cache...");
						_Chain.ResetToGenesis();
					}
				}
				var heightBefore = _Chain.Height;
				using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
				{
					timeout.CancelAfter(_Network.ChainLoadingTimeout);
					Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Trying to connect via the P2P protocol to trusted node ({_ChainConfiguration.NodeEndpoint.ToEndpointString()})...");
					var userAgent = "NBXplorer-" + RandomUtils.GetInt64();
					bool handshaked = false;
					bool connected = false;
					bool chainLoaded = false;
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
							connected = true;
							Logs.Explorer.LogInformation($"{Network.CryptoCode}: TCP Connection succeed, handshaking...");
							node.VersionHandshake(handshakeTimeout.Token);
							handshaked = true;
							Logs.Explorer.LogInformation($"{Network.CryptoCode}: Handshaked");
							var loadChainTimeout = _Network.NBitcoinNetwork.ChainName == ChainName.Regtest ? TimeSpan.FromSeconds(5) : _Network.ChainCacheLoadingTimeout;
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
							chainLoaded = true;
							var peer = (await _OriginalRPC.GetPeersInfoAsync())
										.FirstOrDefault(p => p.SubVersion == userAgent);
							if (peer.IsWhitelisted())
							{
								Logs.Explorer.LogInformation($"{Network.CryptoCode}: NBXplorer is correctly whitelisted by the node");
							}
							else
							{
								var addressStr = peer.Address is IPEndPoint end ? end.Address.ToString() : peer.Address?.ToString();
								Logs.Explorer.LogWarning($"{Network.CryptoCode}: Your NBXplorer server is not whitelisted by your node," +
									$" you should add \"whitelist={addressStr}\" to the configuration file of your node. (Or use whitebind)");
							}
						}
						catch
						{
							if (!connected)
							{
								Logs.Explorer.LogWarning($"{Network.CryptoCode}: NBXplorer failed to connect to the node via P2P ({_ChainConfiguration.NodeEndpoint.ToEndpointString()}).{Environment.NewLine}" +
									$"It may come from: A firewall blocking the traffic, incorrect IP or port, or your node may not have an available connection slot. {Environment.NewLine}" +
									$"To make sure your node have an available connection slot, use \"whitebind\" or \"whitelist\" in your node configuration. (typically whitelist=127.0.0.1 if NBXplorer and the node are on the same machine.){Environment.NewLine}");
							}
							else if (!handshaked)
							{
								Logs.Explorer.LogWarning($"{Network.CryptoCode}: NBXplorer connected to the remote node but failed to handhsake via P2P.{Environment.NewLine}" +
									$"Your node may not have an available connection slot, or you may try to connect to the wrong node. (ie, trying to connect to a LTC node on the BTC configuration).{Environment.NewLine}" +
									$"To make sure your node have an available connection slot, use \"whitebind\" or \"whitelist\" in your node configuration. (typically whitelist=127.0.0.1 if NBXplorer and the node are on the same machine.){Environment.NewLine}");
							}
							else if (!chainLoaded)
							{
								Logs.Explorer.LogWarning($"{Network.CryptoCode}: NBXplorer connected and handshaked the remote node but failed to load the chain of header.{Environment.NewLine}" +
									$"Your connection may be throttled, or you may try to connect to the wrong node. (ie, trying to connect to a LTC node on the BTC configuration).{Environment.NewLine}");
							}
							throw;
						}
					}
				}
				Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Height: " + _Chain.Height);
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
			catch (RPCException r) when (r.RPCCode == RPCErrorCode.RPC_INVALID_ADDRESS_OR_KEY || r.RPCCode == RPCErrorCode.RPC_INVALID_PARAMETER)
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

		public async Task SaveMatches(Transaction transaction)
		{
			var explorerBehavior = GetExplorerBehavior();
			if (explorerBehavior is null)
				return;
			await explorerBehavior.SaveMatches(transaction, false);
		}

		public RPCClient GetConnectedClient()
		{
			if (!RPCAvailable)
				return null;
			return RPC;
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

		public long? SyncHeight
		{
			get
			{
				var loc = GetLocation();
				if (loc is null)
					return null;
				return _Chain.FindFork(loc)?.Height;
			}
		}

		public ILogger Logger { get; }
	}
}
