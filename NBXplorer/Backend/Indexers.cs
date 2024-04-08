using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Behaviors;
using NBitcoin.RPC;
using NBXplorer.Configuration;
using NBXplorer.Events;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NBXplorer.Backend
{
	public class Indexers : IHostedService
	{

		Dictionary<string, Indexer> _Indexers = new Dictionary<string, Indexer>();

		public AddressPoolService AddressPoolService { get; }
		public ILoggerFactory LoggerFactory { get; }
		public IRPCClients RpcClients { get; }
		public ExplorerConfiguration Configuration { get; }
		public NBXplorerNetworkProvider NetworkProvider { get; }
		public RepositoryProvider RepositoryProvider { get; }
		public DbConnectionFactory ConnectionFactory { get; }
		public EventAggregator EventAggregator { get; }

		public Indexers(
			AddressPoolService addressPoolService,
			ILoggerFactory loggerFactory,
			IRPCClients rpcClients,
			ExplorerConfiguration configuration,
			NBXplorerNetworkProvider networkProvider,
			RepositoryProvider repositoryProvider,
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
				_Indexers.Add(config.CryptoCode, new Indexer(
					AddressPoolService,
					LoggerFactory.CreateLogger($"NBXplorer.Indexer.{config.CryptoCode}"),
					network,
					RpcClients.Get(network),
					(Repository)RepositoryProvider.GetRepository(network),
					ConnectionFactory,
					Configuration,
					config,
					EventAggregator));
			}
			await Task.WhenAll(_Indexers.Values.Select(v => ((Indexer)v).StartAsync(cancellationToken)));
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			await Task.WhenAll(_Indexers.Values.Select(v => ((Indexer)v).StopAsync(cancellationToken)));
		}

		public Indexer GetIndexer(NBXplorerNetwork network)
		{
			_Indexers.TryGetValue(network.CryptoCode, out var r);
			return r;
		}

		public IEnumerable<Indexer> All()
		{
			return _Indexers.Values;
		}
	}
}
