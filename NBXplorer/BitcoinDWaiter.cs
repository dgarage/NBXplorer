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

namespace NBXplorer
{
	public enum BitcoinDWaiterState
	{
		NotStarted,
		CoreSynching,
		NBXplorerSynching,
		Ready
	}

	/// <summary>
	/// Hack, ASP.NET core DI does not support having one singleton for multiple interfaces
	/// </summary>
	public class BitcoinDWaitersAccessor
	{
		public BitcoinDWaiters Instance
		{
			get; set;
		}
	}

	public class BitcoinDWaiters : IHostedService
	{
		Dictionary<string, BitcoinDWaiter> _Waiters;
		public BitcoinDWaiters(
							BitcoinDWaitersAccessor accessor,
								NBXplorerNetworkProvider networkProvider,
							  ChainProvider chains,
							  RepositoryProvider repositoryProvider,
							  ExplorerConfiguration config,
							  RPCClientProvider rpcProvider,
							  EventAggregator eventAggregator)
		{
			accessor.Instance = this;
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
												eventAggregator))
				.ToDictionary(s => s.Network.CryptoCode, s => s);
		}
		public Task StartAsync(CancellationToken cancellationToken)
		{
			return Task.WhenAll(_Waiters.Select(s => s.Value.StartAsync(cancellationToken)).ToArray());
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			return Task.WhenAll(_Waiters.Select(s => s.Value.StopAsync(cancellationToken)).ToArray());
		}

		public BitcoinDWaiter GetWaiter(NBXplorerNetwork network)
		{
			return GetWaiter(network.CryptoCode);
		}
		public BitcoinDWaiter GetWaiter(string cryptoCode)
		{
			_Waiters.TryGetValue(cryptoCode, out BitcoinDWaiter waiter);
			return waiter;
		}

		public IEnumerable<BitcoinDWaiter> All()
		{
			return _Waiters.Values;
		}
	}

	public class BitcoinDWaiter : IHostedService
	{
		RPCClient _RPC;
		NBXplorerNetwork _Network;
		ExplorerConfiguration _Configuration;
		SlimChain _Chain;
		private Repository _Repository;
		EventAggregator _EventAggregator;

		public BitcoinDWaiter(
			RPCClient rpc,
			ExplorerConfiguration configuration,
			NBXplorerNetwork network,
			SlimChain chain,
			Repository repository,
			EventAggregator eventAggregator)
		{
			_RPC = rpc;
			_Configuration = configuration;
			_Network = network;
			_Chain = chain;
			_Repository = repository;
			State = BitcoinDWaiterState.NotStarted;
			_EventAggregator = eventAggregator;
		}
		public NodeState NodeState
		{
			get;
			private set;
		}

		private NodesGroup _Group;


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
				return _RPC;
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
			if(_Disposed)
				throw new ObjectDisposedException(nameof(BitcoinDWaiter));

			_Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			_Loop = StartLoop(_Cts.Token);
			_Subscription = _EventAggregator.Subscribe<NewBlockEvent>(s => _Tick.Set());
			return Task.CompletedTask;
		}
		AutoResetEvent _Tick = new AutoResetEvent(false);

		private async Task StartLoop(CancellationToken token)
		{
			try
			{
				while(!token.IsCancellationRequested)
				{
					try
					{
						while(await StepAsync(token))
						{
						}
						await Task.WhenAny(_Tick.WaitOneAsync(), Task.Delay(PollingInterval, token));
					}
					catch(Exception ex) when(!token.IsCancellationRequested)
					{
						Logs.Configuration.LogError(ex, $"{_Network.CryptoCode}: Unhandled exception in BitcoinDWaiter");
						await Task.WhenAny(_Tick.WaitOneAsync(), Task.Delay(TimeSpan.FromSeconds(5.0), token));
					}
				}
			}
			catch when(token.IsCancellationRequested)
			{
			}
		}

		public BlockLocator GetLocation()
		{
			return _Group?.ConnectedNodes?.FirstOrDefault()?.Behaviors?.Find<ExplorerBehavior>()?.CurrentLocation;
		}

		public TimeSpan PollingInterval
		{
			get; set;
		} = TimeSpan.FromMinutes(1.0);

		private void ConnectedNodes_Changed(object sender, NodeEventArgs e)
		{
			_Tick.Set();
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			_Disposed = true;
			_Cts.Cancel();
			_Subscription.Dispose();
			if(_Group != null)
			{
				_Group.ConnectedNodes.Added -= ConnectedNodes_Changed;
				_Group.ConnectedNodes.Removed -= ConnectedNodes_Changed;
				_Group.Disconnect();
				_Group = null;
			}
			State = BitcoinDWaiterState.NotStarted;
			_Chain = null;
			_Tick.Set();
			return _Loop;
		}

		async Task<bool> StepAsync(CancellationToken token)
		{
			var oldState = State;
			switch(State)
			{
				case BitcoinDWaiterState.NotStarted:
					await RPCArgs.TestRPCAsync(_Network, _RPC, token);
					GetBlockchainInfoResponse blockchainInfo = null;
					try
					{
						blockchainInfo = await _RPC.GetBlockchainInfoAsyncEx();
						if(blockchainInfo != null && _Network.NBitcoinNetwork.NetworkType == NetworkType.Regtest)
						{
							if(await WarmupBlockchain())
							{
								blockchainInfo = await _RPC.GetBlockchainInfoAsyncEx();
							}
						}
					}
					catch(Exception ex)
					{
						Logs.Configuration.LogError(ex, $"{_Network.CryptoCode}: Failed to connect to RPC");
						break;
					}
					if(IsSynchingCore(blockchainInfo))
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
						blockchainInfo2 = await _RPC.GetBlockchainInfoAsyncEx();
					}
					catch(Exception ex)
					{
						Logs.Configuration.LogError(ex, $"{_Network.CryptoCode}: Failed to connect to RPC");
						State = BitcoinDWaiterState.NotStarted;
						break;
					}
					if(!IsSynchingCore(blockchainInfo2))
					{
						await ConnectToBitcoinD(token);
						State = BitcoinDWaiterState.NBXplorerSynching;
					}
					break;
				case BitcoinDWaiterState.NBXplorerSynching:
					var explorer = _Group?.ConnectedNodes.SelectMany(n => n.Behaviors.OfType<ExplorerBehavior>()).FirstOrDefault();
					if(explorer == null)
					{
						GetBlockchainInfoResponse blockchainInfo3 = null;
						try
						{
							blockchainInfo3 = await _RPC.GetBlockchainInfoAsyncEx();
						}
						catch(Exception ex)
						{
							Logs.Configuration.LogError(ex, $"{_Network.CryptoCode}: Failed to connect to RPC");
							State = BitcoinDWaiterState.NotStarted;
							break;
						}
						if(IsSynchingCore(blockchainInfo3))
							State = BitcoinDWaiterState.CoreSynching;
					}
					else if(!explorer.IsSynching())
					{
						State = BitcoinDWaiterState.Ready;
					}
					break;
				case BitcoinDWaiterState.Ready:
					var explorer2 = _Group?.ConnectedNodes.SelectMany(n => n.Behaviors.OfType<ExplorerBehavior>()).FirstOrDefault();
					if(explorer2 == null)
					{
						State = BitcoinDWaiterState.NotStarted;
					}
					else if(explorer2.IsSynching())
					{
						State = BitcoinDWaiterState.NBXplorerSynching;
					}
					break;
				default:
					break;
			}
			var changed = oldState != State;

			if(changed)
			{
				_EventAggregator.Publish(new BitcoinDStateChangedEvent(_Network, oldState, State));
			}

			return changed;
		}

		private async Task ConnectToBitcoinD(CancellationToken cancellation)
		{
			if(_Group != null)
				return;
			_Chain.ResetToGenesis();
			if(_Configuration.CacheChain)
				await LoadChainFromCache();
			var heightBefore = _Chain.Height;
			using(var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
			{
				timeout.CancelAfter(_Network.ChainLoadingTimeout);
				try
				{
					await LoadChainFromNode(timeout.Token);
				}
				catch when(!cancellation.IsCancellationRequested)
				{
					throw new OperationCanceledException("Loading the chain from the node timed out", timeout.Token);
				}
			}
			if(_Configuration.CacheChain && heightBefore != _Chain.Height)
			{
				await SaveChainInCache();
			}
			await LoadGroup();
		}

		private async Task LoadGroup()
		{
			AddressManager manager = new AddressManager();
			manager.Add(new NetworkAddress(GetEndpoint()), IPAddress.Loopback);
			NodesGroup group = new NodesGroup(_Network.NBitcoinNetwork, new NodeConnectionParameters()
			{
				Services = NodeServices.Nothing,
				IsRelay = true,
				TemplateBehaviors =
				{
					new AddressManagerBehavior(manager)
					{
						PeersToDiscover = 1,
						Mode = AddressManagerBehaviorMode.None
					},
					new ExplorerBehavior(_Repository, _Chain, _EventAggregator) { StartHeight = _Configuration.ChainConfigurations.First(c => c.CryptoCode == _Network.CryptoCode).StartHeight },
					new SlimChainBehavior(_Chain),
					new PingPongBehavior()
				}
			});
			group.AllowSameGroup = true;
			group.MaximumNodeConnection = 1;

			var task = WaitConnected(group);

			group.Connect();

			try
			{
				await task;
			}
			catch(Exception ex)
			{
				Logs.Configuration.LogError(ex, $"{_Network.CryptoCode}: Failure to connect to the bitcoin node (P2P)");
				throw;
			}
			_Group = group;

			group.ConnectedNodes.Added += ConnectedNodes_Changed;
			group.ConnectedNodes.Removed += ConnectedNodes_Changed;
		}

		private IPEndPoint GetEndpoint()
		{
			return _Configuration.ChainConfigurations.Where(c => c.CryptoCode == Network.CryptoCode).Select(c => c.NodeEndpoint).FirstOrDefault();
		}

		private static async Task WaitConnected(NodesGroup group)
		{
			TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
			EventHandler<NodeEventArgs> waitingConnected = null;
			waitingConnected = (a, b) =>
			{
				tcs.TrySetResult(true);
				group.ConnectedNodes.Added -= waitingConnected;
			};
			group.ConnectedNodes.Added += waitingConnected;
			CancellationTokenSource cts = new CancellationTokenSource(5000);
			using(cts.Token.Register(() => tcs.TrySetCanceled()))
			{
				await tcs.Task;
			}
		}

		private async Task SaveChainInCache()
		{
			var suffix = _Network.CryptoCode == "BTC" ? "" : _Network.CryptoCode;
			var cachePath = Path.Combine(_Configuration.DataDir, $"{suffix}chain-slim.dat");
			var cachePathTemp = Path.Combine(_Configuration.DataDir, $"{suffix}chain-slim.dat.temp");

			Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Saving chain to cache...");
			using(var fs = new FileStream(cachePathTemp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024))
			{
				await _Chain.Save(fs);
				fs.Flush();
			}

			if(File.Exists(cachePath))
				File.Delete(cachePath);
			File.Move(cachePathTemp, cachePath);
			Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Chain cached");
		}

		private async Task LoadChainFromNode(CancellationToken cancellation)
		{
			Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Loading chain from node...");
			var userAgent = "NBXplorer-" + RandomUtils.GetInt64();
			bool handshaked = false;
			using(var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
			{
				try
				{
					handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(10));
					using(var node = Node.Connect(_Network.NBitcoinNetwork, GetEndpoint(), new NodeConnectionParameters()
					{
						UserAgent = userAgent,
						ConnectCancellation = handshakeTimeout.Token,
						IsRelay = false
					}))
					{
						node.VersionHandshake(handshakeTimeout.Token);
						handshaked = true;
						var loadChainTimeout = _Network.NBitcoinNetwork.NetworkType == NetworkType.Regtest ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(15);
						if(_Chain.Height < 5)
							loadChainTimeout = TimeSpan.FromDays(7); // unlimited

						try
						{
							using(var cts1 = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
							{
								cts1.CancelAfter(loadChainTimeout);
								await node.SynchronizeSlimChain(_Chain, cancellationToken: cts1.Token);
							}
						}
						catch // Timeout happens with SynchronizeChain, if so, throw away the cached chain
						{
							_Chain.ResetToGenesis();
							await node.SynchronizeSlimChain(_Chain, cancellationToken: cancellation);
						}


						var peer = (await _RPC.GetPeersInfoAsync())
									.FirstOrDefault(p => p.SubVersion == userAgent);
						if(peer != null && !peer.IsWhiteListed)
						{
							Logs.Explorer.LogWarning($"{Network.CryptoCode}: Your NBXplorer server is not whitelisted by your node," +
								$" you should add \"whitelist={peer.Address.Address}\" to the configuration file of your node. (Or use whitebind)");
						}
					}
				}
				catch(OperationCanceledException) when(!handshaked && handshakeTimeout.IsCancellationRequested)
				{
					Logs.Explorer.LogWarning($"{Network.CryptoCode}: The initial hanshake failed, your NBXplorer server might not be whitelisted by your node," +
							$" if your bitcoin node is on the same machine as NBXplorer, you should add \"whitelist=127.0.0.1\" to the configuration file of your node. (Or use whitebind)");
					throw;
				}
			}
			Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Height: " + _Chain.Height);
		}

		private async Task LoadChainFromCache()
		{
			var suffix = _Network.CryptoCode == "BTC" ? "" : _Network.CryptoCode;
			{
				var legacyCachePath = Path.Combine(_Configuration.DataDir, $"{suffix}chain.dat");
				if(_Configuration.CacheChain && File.Exists(legacyCachePath))
				{
					Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Loading chain from cache...");
					var chain = new ConcurrentChain(_Network.NBitcoinNetwork);
					chain.Load(File.ReadAllBytes(legacyCachePath), _Network.NBitcoinNetwork);
					await LoadSlimAndSaveToSlimFormat(chain);
					File.Delete(legacyCachePath);
					Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Height: " + _Chain.Height);
					return;
				}
			}

			{
				var cachePath = Path.Combine(_Configuration.DataDir, $"{suffix}chain-stripped.dat");
				if(_Configuration.CacheChain && File.Exists(cachePath))
				{
					Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Loading chain from cache...");
					var chain = new ConcurrentChain(_Network.NBitcoinNetwork);
					chain.Load(File.ReadAllBytes(cachePath), _Network.NBitcoinNetwork, new ConcurrentChain.ChainSerializationFormat()
					{
						SerializeBlockHeader = false,
						SerializePrecomputedBlockHash = true,
					});
					await LoadSlimAndSaveToSlimFormat(chain);
					File.Delete(cachePath);
					Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Height: " + _Chain.Height);
					return;
				}
			}

			{
				var slimCachePath = Path.Combine(_Configuration.DataDir, $"{suffix}chain-slim.dat");
				if(_Configuration.CacheChain && File.Exists(slimCachePath))
				{
					Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Loading chain from cache...");
					using(var file = new FileStream($"{suffix}chain-slim.dat", FileMode.Open, FileAccess.Read, FileShare.None, 1024 * 1024))
					{
						await _Chain.Load(file);
					}
					Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Height: " + _Chain.Height);
					return;
				}
			}
		}

		private async Task LoadSlimAndSaveToSlimFormat(ConcurrentChain chain)
		{
			foreach(var block in chain.ToEnumerable(false))
			{
				_Chain.TrySetTip(block.HashBlock, block.Previous?.HashBlock ?? uint256.Zero);
			}
			await SaveChainInCache();
		}

		private async Task<bool> WarmupBlockchain()
		{
			if(await _RPC.GetBlockCountAsync() < _Network.NBitcoinNetwork.Consensus.CoinbaseMaturity)
			{
				Logs.Configuration.LogInformation($"{_Network.CryptoCode}: Less than {_Network.NBitcoinNetwork.Consensus.CoinbaseMaturity} blocks, mining some block for regtest");
				await _RPC.EnsureGenerateAsync(_Network.NBitcoinNetwork.Consensus.CoinbaseMaturity + 1);
				return true;
			}
			else
			{
				var hash = await _RPC.GetBestBlockHashAsync();

				BlockHeader header = null;
				try
				{
					header = await _RPC.GetBlockHeaderAsync(hash);
				}
				catch(RPCException ex) when(ex.RPCCode == RPCErrorCode.RPC_METHOD_NOT_FOUND)
				{
					header = (await _RPC.GetBlockAsync(hash)).Header;
				}
				if((DateTimeOffset.UtcNow - header.BlockTime) > TimeSpan.FromSeconds(24 * 60 * 60))
				{
					Logs.Configuration.LogInformation($"{_Network.CryptoCode}: It has been a while nothing got mined on regtest... mining 10 blocks");
					await _RPC.GenerateAsync(10);
					return true;
				}
				return false;
			}
		}

		public bool IsSynchingCore(GetBlockchainInfoResponse blockchainInfo)
		{
			if(blockchainInfo.InitialBlockDownload.HasValue)
				return blockchainInfo.InitialBlockDownload.Value;
			if(blockchainInfo.MedianTime.HasValue && _Network.NBitcoinNetwork.NetworkType != NetworkType.Regtest)
			{
				var time = NBitcoin.Utils.UnixTimeToDateTime(blockchainInfo.MedianTime.Value);
				// 5 month diff? probably synching...
				if(DateTimeOffset.UtcNow - time > TimeSpan.FromDays(30 * 5))
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
				return _Group?.ConnectedNodes.Count != 0;
			}
		}

	}
}
