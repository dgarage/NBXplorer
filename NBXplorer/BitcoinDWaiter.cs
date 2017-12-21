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
		CancellationTokenSource _Stop = new CancellationTokenSource();
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
		public Task StartAsync(CancellationToken cancellationToken)
		{
			if(_Disposed)
				throw new ObjectDisposedException(nameof(BitcoinDWaiter));
			_Timer = new Timer(Callback, null, 0, (int)TimeSpan.FromMinutes(1.0).TotalMilliseconds);
			_Subscription = _EventAggregator.Subscribe<NewBlockEvent>(async s => await RunStepsAsync());
			return Task.CompletedTask;
		}

		void Callback(object state)
		{
			RunStepsAsync().GetAwaiter().GetResult();
		}

		private async void ConnectedNodes_Changed(object sender, NodeEventArgs e)
		{
			await RunStepsAsync();
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			_Disposed = true;
			_Timer.Dispose();
			_Subscription.Dispose();
			_Stop.Cancel();
			_Idle.Wait();
			if(_Group != null)
			{
				_Group.ConnectedNodes.Added -= ConnectedNodes_Changed;
				_Group.ConnectedNodes.Removed -= ConnectedNodes_Changed;
				_Group.Disconnect();
				_Group = null;
			}
			State = BitcoinDWaiterState.NotStarted;
			_Chain = null;
			return Task.CompletedTask;
		}


		ManualResetEventSlim _Idle = new ManualResetEventSlim(true);
		async Task RunStepsAsync()
		{
			if(!_Idle.IsSet)
				return;
			try
			{
				_Idle.Reset();
				while(await StepAsync())
				{
				}
			}
			catch(Exception ex)
			{
				if(!_Stop.IsCancellationRequested)
					Logs.Configuration.LogError(ex, "Error while synching with the node");
			}
			finally
			{
				_Idle.Set();
			}
		}

		private void SetInterval(TimeSpan interval)
		{
			try
			{
				_Timer.Change(0, (int)interval.TotalMilliseconds);
			}
			catch { }
		}

		async Task<bool> StepAsync()
		{
			if(_Disposed)
				return false;
			var oldState = State;
			switch(State)
			{
				case BitcoinDWaiterState.NotStarted:
					await RPCArgs.TestRPCAsync(_Network, _RPC);
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
						await ConnectToBitcoinD();
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
						await ConnectToBitcoinD();
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

		private async Task ConnectToBitcoinD()
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
			LoadChainFromNode();
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
			Logs.Configuration.LogInformation($"Saving chain to cache...");
			var ms = new MemoryStream();
			_Chain.WriteTo(ms);
			File.WriteAllBytes(cachePath, ms.ToArray());
			Logs.Configuration.LogInformation($"Saved");
		}

		private void LoadChainFromNode()
		{
			Logs.Configuration.LogInformation($"Loading chain from node...");
			using(var node = Node.Connect(_Network.Network, _Configuration.NodeEndpoint))
			{
				using(var cts = new CancellationTokenSource(5000))
				{
					using(var cts2 = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, _Stop.Token))
					{
						node.VersionHandshake(cts2.Token);
					}
					node.SynchronizeChain(_Chain, cancellationToken: _Stop.Token);
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
		private Timer _Timer;

		public bool Connected
		{
			get
			{
				return _Group?.ConnectedNodes.Count != 0;
			}
		}

	}
}
