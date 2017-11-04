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

namespace NBXplorer
{
	public class NBxplorerInitializer : IDisposable
	{
		RPCClient _RPC;
		NetworkInformation _Network;
		ExplorerConfiguration _Configuration;
		ConcurrentChain _Chain;
		ChainEvents _Events;
		private Repository _Repository;

		public NBxplorerInitializer(
			RPCClient rpc,
			ExplorerConfiguration configuration,
			ConcurrentChain chain,
			Repository repository,
			CallbackInvoker invoker,
			ChainEvents events)
		{
			_RPC = rpc;
			_Configuration = configuration;
			_Network = _Configuration.Network;
			_Events = events;
			_Chain = chain;
			_Repository = repository;
			_Invoker = invoker;
		}

		private CallbackInvoker _Invoker;
		private NodesGroup _Group;

		public async Task<bool> TestAsync()
		{
			var passed = true;
			try
			{
				await RPCArgs.TestRPCAsync(_Network.Network, _RPC);
			}
			catch(Exception ex)
			{
				Log(ex);
				passed = false;
			}

			try
			{
				Logs.Configuration.LogInformation("Trying to connect to node: " + _Configuration.NodeEndpoint);
				using(var node = Node.Connect(_Network.Network, _Configuration.NodeEndpoint))
				{
					var cts = new CancellationTokenSource();
					cts.CancelAfter(5000);
					node.VersionHandshake(cts.Token);
					Logs.Configuration.LogInformation("Handshaked");
				}
				Logs.Configuration.LogInformation("Node connection successfull");
			}
			catch(Exception ex)
			{
				Log(ex);
				passed = false;
			}

			return passed;
		}

		private void Log(Exception ex)
		{
			if(ex.Message != null)
			{
				Logs.Configuration.LogError(ex, "Error while testing configation");
			}
		}

		ManualResetEventSlim _Starting = new ManualResetEventSlim(true);
		public async Task StartAsync()
		{
			if(_Disposed)
				throw new ObjectDisposedException(nameof(NBxplorerInitializer));
			_Starting.Reset();
			try
			{

				if(_Network.IsRegTest)
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

				var cachePath = Path.Combine(_Configuration.DataDir, "chain.dat");
				if(_Configuration.CacheChain && File.Exists(cachePath))
				{
					Logs.Configuration.LogInformation($"Loading chain from cache...");
					_Chain.Load(File.ReadAllBytes(cachePath));
					Logs.Configuration.LogInformation($"Height: " + _Chain.Height);
				}

				var heightBefore = _Chain.Height;
				Logs.Configuration.LogInformation($"Loading chain from node...");
				using(var node = Node.Connect(_Network.Network, _Configuration.NodeEndpoint))
				{
					var cts = new CancellationTokenSource();
					cts.CancelAfter(5000);
					node.VersionHandshake(cts.Token);
					node.SynchronizeChain(_Chain);
				}
				Logs.Configuration.LogInformation("Height: " + _Chain.Height);

				if(_Configuration.CacheChain && heightBefore != _Chain.Height)
				{
					Logs.Configuration.LogInformation($"Saving chain to cache...");
					var ms = new MemoryStream();
					_Chain.WriteTo(ms);
					File.WriteAllBytes(cachePath, ms.ToArray());
					Logs.Configuration.LogInformation($"Saved");
				}


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
					}
				}
				});
				group.AllowSameGroup = true;
				group.MaximumNodeConnection = 1;
				group.Connect();
				_Group = group;
			}
			finally { _Starting.Set(); }
		}

		bool _Disposed = false;

		public bool Connected
		{
			get
			{
				return _Group?.ConnectedNodes.Count != 0;
			}
		}

		public void Dispose()
		{
			_Disposed = true;
			_Starting.Wait();
			if(_Group != null)
			{
				_Group.Disconnect();
				_Group = null;
			}
		}
	}
}
