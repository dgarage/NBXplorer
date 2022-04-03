using Microsoft.Extensions.Diagnostics.HealthChecks;
using NBXplorer.Backends;
using NBXplorer.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer.HealthChecks
{
	public class NodesHealthCheck : IHealthCheck
	{
		public NodesHealthCheck(
			NBXplorerNetworkProvider networkProvider,
			IIndexers indexers)
		{
			NetworkProvider = networkProvider;
			Indexers = indexers;
		}

		public NBXplorerNetworkProvider NetworkProvider { get; }
		public IIndexers Indexers { get; }

		public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
		{
			bool ok = true;
			var data = new Dictionary<string, object>();
			foreach (var indexer in Indexers.All())
			{
				ok &= indexer.GetConnectedClient() is not null;
				data.Add(indexer.Network.CryptoCode, indexer.State.ToString());
			}
			return Task.FromResult(ok ? HealthCheckResult.Healthy(data: data) : HealthCheckResult.Degraded("Some nodes are not running", data: data));
		}
	}
}
