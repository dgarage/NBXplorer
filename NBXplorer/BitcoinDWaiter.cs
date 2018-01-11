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
	public class BitcoinDWaiterAccessor
	{
		public BitcoinDWaiter Instance
		{
			get; set;
		}
	}

	public class BitcoinDWaiter : IHostedService
	{
		RPCClient _RPC;
		NetworkInformation _Network;
		ExplorerConfiguration _Configuration;
		ConcurrentChain _Chain;
		private Repository _Repository;
		EventAggregator _EventAggregator;

		public BitcoinDWaiter(
			RPCClient rpc,
			ExplorerConfiguration configuration,
			ConcurrentChain chain,
			Repository repository,
			CallbackInvoker invoker,
			EventAggregator eventAggregator,
			BitcoinDWaiterAccessor accessor)
		{
			_RPC = rpc;
			_Configuration = configuration;
			_Network = _Configuration.Network;
			_Chain = chain;
			_Repository = repository;
			_Invoker = invoker;
			State = BitcoinDWaiterState.NotStarted;
			_EventAggregator = eventAggregator;
			accessor.Instance = this;
		}
		public NodeState NodeState
		{
			get;
			private set;
		}

		private CallbackInvoker _Invoker;
		private NodesGroup _Group;


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
						Logs.Configuration.LogError(ex, "Unhandled exception in BitcoinDWaiter");
						await Task.WhenAny(_Tick.WaitOneAsync(), Task.Delay(TimeSpan.FromSeconds(5.0), token));
					}
				}
			}
			catch when(token.IsCancellationRequested)
			{
			}
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
						blockchainInfo = await _RPC.GetBlockchainInfoAsync();
					}
					catch(Exception ex)
					{
						Logs.Configuration.LogError(ex, "Failed to connect to RPC");
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
						blockchainInfo2 = await _RPC.GetBlockchainInfoAsync();
					}
					catch(Exception ex)
					{
						Logs.Configuration.LogError(ex, "Failed to connect to RPC");
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
							blockchainInfo3 = await _RPC.GetBlockchainInfoAsync();
						}
						catch(Exception ex)
						{
							Logs.Configuration.LogError(ex, "Failed to connect to RPC");
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
				_EventAggregator.Publish(new BitcoinDStateChangedEvent(oldState, State));
			}

			return changed;
		}

		private async Task ConnectToBitcoinD(CancellationToken cancellation)
		{
			if(_Network.IsRegTest)
			{
				await WarmupBlockchain();
			}
			if(_Group != null)
				return;
			if(_Configuration.CacheChain)
				LoadChainFromCache();
			var heightBefore = _Chain.Height;
			LoadChainFromNode(cancellation);
			if(_Configuration.CacheChain && heightBefore != _Chain.Height)
			{
				SaveChainInCache();
			}
			await LoadGroup();
		}

		private async Task LoadGroup()
		{
			AddressManager manager = new AddressManager();
			manager.Add(new NetworkAddress(_Configuration.NodeEndpoint), IPAddress.Loopback);
			NodesGroup group = new NodesGroup(_Network.Network, new NodeConnectionParameters()
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
					new ExplorerBehavior(_Repository, _Chain, _Invoker, _EventAggregator) { StartHeight = _Configuration.StartHeight },
					new ChainBehavior(_Chain)
					{
						CanRespondToGetHeaders = false
					},
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
				Logs.Configuration.LogError(ex, "Failure to connect to the bitcoin node (P2P)");
				throw;
			}
			_Group = group;

			group.ConnectedNodes.Added += ConnectedNodes_Changed;
			group.ConnectedNodes.Removed += ConnectedNodes_Changed;
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

		private void SaveChainInCache()
		{
			var cachePath = Path.Combine(_Configuration.DataDir, "chain.dat");
			var cachePathTemp = Path.Combine(_Configuration.DataDir, "chain.dat.temp");

			Logs.Configuration.LogInformation($"Saving chain to cache...");
			using(var fs = new FileStream(cachePathTemp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024))
			{
				_Chain.WriteTo(fs);
				fs.Flush();
			}

			if(File.Exists(cachePath))
				File.Delete(cachePath);
			File.Move(cachePathTemp, cachePath);
			Logs.Configuration.LogInformation($"Saved");
		}

		private void LoadChainFromNode(CancellationToken cancellation)
		{
			Logs.Configuration.LogInformation($"Loading chain from node...");
			using(var node = Node.Connect(_Network.Network, _Configuration.NodeEndpoint))
			{
				using(var cts = new CancellationTokenSource(5000))
				{
					using(var cts2 = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellation))
					{
						node.VersionHandshake(cts2.Token);
					}
					if(!_Network.IsRegTest)
						node.SynchronizeChain(_Chain, cancellationToken: cancellation);
					else
					{
						// Regtest get stucks sometimes, so we need to start fresh...
						try
						{
							node.SynchronizeChain(_Chain, cancellationToken: new CancellationTokenSource(10000).Token);
						}
						catch
						{
							_Chain = new ConcurrentChain(_Network.Network);
							node.SynchronizeChain(_Chain, cancellationToken: cancellation);
						}
					}
				}
			}
			Logs.Configuration.LogInformation("Height: " + _Chain.Height);
		}

		private void LoadChainFromCache()
		{
			var cachePath = Path.Combine(_Configuration.DataDir, "chain.dat");
			if(_Configuration.CacheChain && File.Exists(cachePath))
			{
				Logs.Configuration.LogInformation($"Loading chain from cache...");
				_Chain.Load(File.ReadAllBytes(cachePath));
				Logs.Configuration.LogInformation($"Height: " + _Chain.Height);
			}
		}

		private async Task WarmupBlockchain()
		{
			if(await _RPC.GetBlockCountAsync() < 100)
			{

				Logs.Configuration.LogInformation($"Less than 100 blocks, mining some block for regtest");
				await _RPC.GenerateAsync(101);
			}
			else
			{
				var header = await _RPC.GetBlockHeaderAsync(await _RPC.GetBestBlockHashAsync());
				if((DateTimeOffset.UtcNow - header.BlockTime) > TimeSpan.FromSeconds(24 * 60 * 60))
				{
					Logs.Configuration.LogInformation($"It has been a while nothing got mined on regtest... mining 10 blocks");
					await _RPC.GenerateAsync(10);
				}
			}
		}

		public static bool IsSynchingCore(GetBlockchainInfoResponse blockchainInfo)
		{
			if(blockchainInfo.InitialBlockDownload.HasValue)
				return blockchainInfo.InitialBlockDownload.Value;
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
