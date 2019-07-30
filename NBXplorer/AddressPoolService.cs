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
		class RefillPoolRequest
		{
			public DerivationStrategyBase DerivationStrategy { get; set; }
			public DerivationFeature Feature { get; set; }
			public TaskCompletionSource<bool> Done { get; set; }
			public int? MinAddresses { get; set; }
		}
		class AddressPool
		{
			Repository _Repository;
			Task _Task;
			internal Channel<RefillPoolRequest> _Channel = Channel.CreateUnbounded<RefillPoolRequest>();

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

			private async Task Listen()
			{
				while (await _Channel.Reader.WaitToReadAsync())
				{
					List<RefillPoolRequest> pendingRequests = new List<RefillPoolRequest>();
					try
					{
						_Channel.Reader.TryRead(out var modelItem);
						if (modelItem == null)
							continue;
						retry:
						RefillPoolRequest nextItem = null;
						pendingRequests.Add(modelItem);

						// Try to batch several requests together
						while (_Channel.Reader.TryRead(out var item))
						{
							if (modelItem.DerivationStrategy == item.DerivationStrategy &&
								modelItem.Feature == item.Feature)
							{
								pendingRequests.Add(item);
							}
							else
							{
								nextItem = item;
								break;
							}
						}

						var query = new GenerateAddressQuery();
						foreach (var i in pendingRequests.Where(o => o.MinAddresses is int).Select(o => o.MinAddresses.Value))
						{
							if (query.MinAddresses is int min)
							{
								query.MinAddresses = min + i;
							}
							else
							{
								query.MinAddresses = new int?(i);
							}
						}

						if (query.MinAddresses is int)
						{
							query.MinAddresses = Math.Min(1000, query.MinAddresses.Value);
						}

						var c = await _Repository.GenerateAddresses(modelItem.DerivationStrategy, modelItem.Feature, query);
						pendingRequests.ForEach(i => i.Done?.TrySetResult(true));
						if (nextItem != null)
						{
							modelItem = nextItem;
							pendingRequests.Clear();
							goto retry;
						}
					}
					catch (Exception ex)
					{
						pendingRequests.ForEach(i => i.Done?.TrySetException(ex));
						Logs.Explorer.LogError(ex, $"{_Repository.Network.CryptoCode}: Error in the Listen of the AddressPoolService");
						await Task.Delay(1000);
					}
				}
			}
		}

		public AddressPoolService(NBXplorerNetworkProvider networks, RepositoryProvider repositoryProvider, KeyPathTemplates keyPathTemplates, AddressPoolServiceAccessor accessor)
		{
			accessor.Instance = this;
			_AddressPoolByNetwork = networks.GetAll().ToDictionary(o => o, o => new AddressPool(repositoryProvider.GetRepository(o)));
			this.keyPathTemplates = keyPathTemplates;
		}
		Dictionary<NBXplorerNetwork, AddressPool> _AddressPoolByNetwork;
		private readonly KeyPathTemplates keyPathTemplates;

		public Task StartAsync(CancellationToken cancellationToken)
		{
			return Task.WhenAll(_AddressPoolByNetwork.Select(kv => kv.Value.StartAsync(cancellationToken)));
		}

		public Task GenerateAddresses(NBXplorerNetwork network, DerivationStrategyBase derivationStrategy, DerivationFeature feature, int? minAddresses = null)
		{
			var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			if (!_AddressPoolByNetwork[network]._Channel.Writer.TryWrite(new RefillPoolRequest() { DerivationStrategy = derivationStrategy, Feature = feature, Done = completion, MinAddresses = minAddresses }))
				completion.TrySetCanceled();
			return completion.Task;
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			return Task.WhenAll(_AddressPoolByNetwork.Select(kv => kv.Value.StopAsync(cancellationToken)));
		}

		internal Task GenerateAddresses(NBXplorerNetwork network, TrackedTransaction[] matches)
		{
			List<Task> refill = new List<Task>();
			foreach (var m in matches)
			{
				var derivationStrategy = (m.TrackedSource as Models.DerivationSchemeTrackedSource)?.DerivationStrategy;
				if (derivationStrategy == null)
					continue;
				foreach (var feature in m.KnownKeyPathMapping.Select(kv => keyPathTemplates.GetDerivationFeature(kv.Value)))
				{
					refill.Add(GenerateAddresses(network, derivationStrategy, feature));
				}
			}
			return Task.WhenAll(refill.ToArray());
		}
	}
}
