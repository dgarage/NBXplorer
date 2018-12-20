using Microsoft.Extensions.Hosting;
using NBXplorer.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class NBXplorerContextFactoryHostedService : IHostedService
	{
		public NBXplorerContextFactoryHostedService(NBXplorerContextFactory contextFactory)
		{
			ContextFactory = contextFactory;
		}

		public NBXplorerContextFactory ContextFactory { get; }

		public Task StartAsync(CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			return ContextFactory.DisposeAsync();
		}
	}
}
