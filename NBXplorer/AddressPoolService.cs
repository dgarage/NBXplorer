using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBXplorer.Backend;
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
	public class AddressPoolService : IHostedService
	{
		class RefillPoolRequest
		{
			public DerivationStrategyBase DerivationStrategy { get; set; }
			public DerivationFeature Feature { get; set; }
			public TaskCompletionSource<bool> Done { get; set; }
			public GenerateAddressQuery GenerateAddressQuery { get; set; }
		}
		class AddressPool
		{
			Repository _Repository;
			Task _Task;
			CancellationTokenSource _Cts;
			internal Channel<RefillPoolRequest> _Channel = Channel.CreateUnbounded<RefillPoolRequest>();

			public AddressPool(Repository repository)
			{
				_Repository = repository;
			}
			public Task StartAsync(CancellationToken cancellationToken)
			{
				_Cts = new CancellationTokenSource();
				_Task = Listen(_Cts.Token);
				return Task.CompletedTask;
			}
			public Task StopAsync(CancellationToken cancellationToken)
			{
				_Channel.Writer.Complete();
				_Cts.Cancel();
				return _Task;
			}

			private async Task Listen(CancellationToken cancellationToken)
			{
				while (await _Channel.Reader.WaitToReadAsync(cancellationToken))
				{
					RefillPoolRequest modelItem = null;
					try
					{
						_Channel.Reader.TryRead(out modelItem);
						if (modelItem == null)
							continue;
						var c = await _Repository.GenerateAddresses(modelItem.DerivationStrategy, modelItem.Feature, modelItem.GenerateAddressQuery);
						modelItem.Done?.TrySetResult(true);
					}
					catch (Exception ex)
					{
						modelItem?.Done.TrySetException(ex);
						Logs.Explorer.LogError(ex, $"{_Repository.Network.CryptoCode}: Error in the Listen of the AddressPoolService");
						await Task.Delay(1000, cancellationToken);
					}
				}
			}
		}

		public AddressPoolService(NBXplorerNetworkProvider networks, RepositoryProvider repositoryProvider, KeyPathTemplates keyPathTemplates)
		{
			this.networks = networks;
			this.repositoryProvider = repositoryProvider;
			this.keyPathTemplates = keyPathTemplates;
		}
		Dictionary<NBXplorerNetwork, AddressPool> _AddressPoolByNetwork;
		private readonly NBXplorerNetworkProvider networks;
		private readonly RepositoryProvider repositoryProvider;
		private readonly KeyPathTemplates keyPathTemplates;

		public async Task StartAsync(CancellationToken cancellationToken)
		{
			await repositoryProvider.StartCompletion;
			_AddressPoolByNetwork = networks.GetAll().ToDictionary(o => o, o => new AddressPool(repositoryProvider.GetRepository(o)));
			await Task.WhenAll(_AddressPoolByNetwork.Select(kv => kv.Value.StartAsync(cancellationToken)));
		}

		public Task GenerateAddresses(NBXplorerNetwork network, DerivationStrategyBase derivationStrategy, DerivationFeature feature, GenerateAddressQuery generateAddressQuery = null)
		{
			var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			if (!_AddressPoolByNetwork[network]._Channel.Writer.TryWrite(new RefillPoolRequest() { DerivationStrategy = derivationStrategy, Feature = feature, Done = completion, GenerateAddressQuery = generateAddressQuery }))
				completion.TrySetCanceled();
			return completion.Task;
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			try
			{
				await Task.WhenAll(_AddressPoolByNetwork.Select(kv => kv.Value.StopAsync(cancellationToken)));
			}
			catch { }
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
