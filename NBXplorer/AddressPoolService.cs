using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBXplorer.DerivationStrategy;
using NBXplorer.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NBXplorer
{
	/// <summary>
	/// Hack, ASP.NET core DI does not support having one singleton for multiple interfaces
	/// </summary>
	public class AddressPoolServiceAccessor
	{
		public AddressPoolService Instance
		{
			get; set;
		}
	}
	public class AddressPoolService : IHostedService
	{
		class AddressPool
		{
			Repository _Repository;
			Task _Task;
			internal Channel<(DerivationStrategyBase DerivationStrategy, DerivationFeature Feature)> _Channel = Channel.CreateUnbounded<(DerivationStrategyBase DerivationStrategy, DerivationFeature Feature)>();

			public AddressPool(Repository repository)
			{
				_Repository = repository;
			}
			public Task StartAsync(CancellationToken cancellationToken)
			{
				_Task = Listen();
				return Task.CompletedTask;
			}
			public Task StopAsync(CancellationToken cancellationToken)
			{
				_Channel.Writer.Complete();
				return _Channel.Reader.Completion;
			}
			const int RefillBatchSize = 3;
			private async Task Listen()
			{
				while (await _Channel.Reader.WaitToReadAsync() && _Channel.Reader.TryRead(out var item))
				{

					int generated = 0;
					while (true)
					{
						var count = await _Repository.RefillAddressPoolIfNeeded(item.DerivationStrategy, item.Feature, RefillBatchSize);
						if (count == 0)
							break;
						generated += count;
					}
				}
			}
		}

		public AddressPoolService(NBXplorerNetworkProvider networks, RepositoryProvider repositoryProvider, AddressPoolServiceAccessor accessor)
		{
			accessor.Instance = this;
			_AddressPoolByNetwork = networks.GetAll().ToDictionary(o => o, o => new AddressPool(repositoryProvider.GetRepository(o)));
		}
		Dictionary<NBXplorerNetwork, AddressPool> _AddressPoolByNetwork;
		public Task StartAsync(CancellationToken cancellationToken)
		{
			return Task.WhenAll(_AddressPoolByNetwork.Select(kv => kv.Value.StartAsync(cancellationToken)));
		}

		public void RefillAddressPoolIfNeeded(NBXplorerNetwork network, DerivationStrategyBase derivationStrategy, DerivationFeature feature)
		{
			_AddressPoolByNetwork[network]._Channel.Writer.TryWrite((derivationStrategy, feature));
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			return Task.WhenAll(_AddressPoolByNetwork.Select(kv => kv.Value.StopAsync(cancellationToken)));
		}

		internal void RefillAddressPoolIfNeeded(NBXplorerNetwork network, TrackedTransaction[] matches)
		{
			foreach(var m in matches)
			{
				var derivationStrategy = (m.TrackedSource as Models.DerivationSchemeTrackedSource)?.DerivationStrategy;
				if (derivationStrategy == null)
						continue;
				foreach (var feature in m.KnownKeyPathMapping.Select(kv => DerivationStrategyBase.GetFeature(kv.Value)))
				{
					RefillAddressPoolIfNeeded(network, derivationStrategy, feature);
				}
			}
		}
	}
}
