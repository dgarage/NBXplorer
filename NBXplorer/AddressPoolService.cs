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
		RepositoryProvider _RepositoryProvider;
		public AddressPoolService(RepositoryProvider repositoryProvider, AddressPoolServiceAccessor accessor)
		{
			accessor.Instance = this;
			_RepositoryProvider = repositoryProvider;
		}
		Task _Task;
		public Task StartAsync(CancellationToken cancellationToken)
		{
			_Task = Listen();
			return Task.CompletedTask;
		}

		Channel<(NBXplorerNetwork Network, DerivationStrategyBase DerivationStrategy, DerivationFeature Feature)> _Channel = Channel.CreateUnbounded<(NBXplorerNetwork Network, DerivationStrategyBase DerivationStrategy, DerivationFeature Feature)>();

		public void RefillAddressPoolIfNeeded(NBXplorerNetwork network, DerivationStrategyBase derivationStrategy, DerivationFeature feature)
		{
			_Channel.Writer.TryWrite((network, derivationStrategy, feature));
		}

		const int RefillBatchSize = 3;
		private async Task Listen()
		{
			while(await _Channel.Reader.WaitToReadAsync() && _Channel.Reader.TryRead(out var item))
			{
				var repo = _RepositoryProvider.GetRepository(item.Network);
				int generated = 0;
				while(true)
				{
					var count = await repo.RefillAddressPoolIfNeeded(item.DerivationStrategy, item.Feature, RefillBatchSize);
					if(count == 0)
						break;
					generated += count;
				}
				//if(generated != 0)
				//	Logs.Explorer.LogInformation($"{item.Network.CryptoCode}: Generated {generated} addresses for {item.DerivationStrategy.ToPrettyStrategyString()} ({item.Feature})");
			}
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			_Channel.Writer.Complete();
			return _Channel.Reader.Completion;
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
