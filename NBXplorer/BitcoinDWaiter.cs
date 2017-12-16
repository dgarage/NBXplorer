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

namespace NBXplorer
{
	public enum BitcoinDWaiterState
	{
		Unknown,
		NotStarted,
		Synching,
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
		ChainEvents _Events;
		private Repository _Repository;

		public BitcoinDWaiter(
			RPCClient rpc,
			ExplorerConfiguration configuration,
			ConcurrentChain chain,
			Repository repository,
			CallbackInvoker invoker,
			ChainEvents events,
			BitcoinDWaiterAccessor accessor)
		{
			_RPC = rpc;
			_Configuration = configuration;
			_Network = _Configuration.Network;
			_Events = events;
			_Chain = chain;
			_Repository = repository;
			_Invoker = invoker;
			State = BitcoinDWaiterState.Unknown;
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



		public bool NodeStarted
		{
			get
			{
				return State == BitcoinDWaiterState.Ready || State == BitcoinDWaiterState.Synching;
			}
		}

		public Task StartAsync(CancellationToken cancellationToken)
		{
			if(_Disposed)
				throw new ObjectDisposedException(nameof(BitcoinDWaiter));
			_Timer = new Timer(Callback, null, 0, (int)TimeSpan.FromMinutes(1.0).TotalMilliseconds);
			return Task.CompletedTask;
		}

		void Callback(object state)
		{
			RunStepsAsync().GetAwaiter().GetResult();
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			_Disposed = true;
			_Timer.Dispose();
			_Idle.Wait();
			if(_Group != null)
			{
				_Group.Disconnect();
				_Group = null;
			}
			State = BitcoinDWaiterState.Unknown;
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
				Logs.Configuration.LogError(ex, "Error while synching with the node");
			}
			finally
			{
				_Idle.Set();
			}
		}

		async Task<bool> StepAsync()
		{
			var oldState = State;
			switch(State)
			{
				case BitcoinDWaiterState.Unknown:
					State = BitcoinDWaiterState.NotStarted;
					break;
				case BitcoinDWaiterState.NotStarted:
					await RPCArgs.TestRPCAsync(_Network.Network, _RPC);
					var blockchainInfo = await _RPC.GetBlockchainInfoAsync();
					if(IsSynchingCore(blockchainInfo))
					{
						State = BitcoinDWaiterState.Synching;
					}
					else
					{
						await ConnectToBitcoinD();
						State = BitcoinDWaiterState.Ready;
					}
					break;
				case BitcoinDWaiterState.Synching:
					var blockchainInfo2 = await _RPC.GetBlockchainInfoAsync();
					if(!IsSynchingCore(blockchainInfo2))
					{
						await ConnectToBitcoinD();
						State = BitcoinDWaiterState.Ready;
					}
					break;
				default:
					break;
			}
			var changed = oldState != State;

			if(changed)
			{
				Logs.Configuration.LogInformation($"BitcoinDWaiter state changed: {oldState} => {State}");
			}

			return changed;
		}

		private async Task ConnectToBitcoinD()
		{
			if(_Network.IsRegTest)
			{
				await WarmupBlockchain();
			}
			LoadChainFromCache();
			var heightBefore = _Chain.Height;
			LoadChainFromNode();
			if(_Configuration.CacheChain && heightBefore != _Chain.Height)
			{
				SaveChainInCache();
			}
			LoadGroup();
		}

		private void LoadGroup()
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
					new ExplorerBehavior(_Repository, _Chain, _Invoker, _Events) { StartHeight = _Configuration.StartHeight },
					new ChainBehavior(_Chain)
					{
						CanRespondToGetHeaders = false
					},
					new PingPongBehavior()
				}
			});
			group.AllowSameGroup = true;
			group.MaximumNodeConnection = 1;
			group.Connect();
			if(_Group != null)
				_Group.Dispose();
			_Group = group;
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
				var cts = new CancellationTokenSource();
				cts.CancelAfter(5000);
				node.VersionHandshake(cts.Token);
				node.SynchronizeChain(_Chain);
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
