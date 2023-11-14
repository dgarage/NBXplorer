using Microsoft.Extensions.Hosting;
using NBXplorer.Configuration;
using NBXplorer.Events;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NBXplorer.Backends;

namespace NBXplorer.HostedServices
{
	/// <summary>
	/// If a signalfilesdir is specified, this service will create or delete a file to signal if the explorer is synched.
	/// </summary>
	public class RPCReadyFileHostedService : IHostedService
	{
		public RPCReadyFileHostedService(EventAggregator eventAggregator, IIndexers indexers, ExplorerConfiguration explorerConfiguration)
		{
			EventAggregator = eventAggregator;
			Indexers = indexers;
			ExplorerConfiguration = explorerConfiguration;
		}

		public EventAggregator EventAggregator { get; }
		public IIndexers Indexers { get; }
		public ExplorerConfiguration ExplorerConfiguration { get; }

		IDisposable disposable;
		public Task StartAsync(CancellationToken cancellationToken)
		{
			if (ExplorerConfiguration.SignalFilesDir is null)
				return Task.CompletedTask;
			disposable = EventAggregator.Subscribe<BitcoinDStateChangedEvent>(state =>
			{
				if (state.NewState != BitcoinDWaiterState.Ready)
					EnsureRPCReadyFileDeleted(state.Network);
				else
					CreateRPCReadyFile(state.Network);
			});
			foreach (var indexer in Indexers.All())
				EnsureRPCReadyFileDeleted(indexer.Network);
			return Task.CompletedTask;
		}

		private void CreateRPCReadyFile(NBXplorerNetwork network)
		{
			var file = GetFilePath(network);
			File.WriteAllText(file, NBitcoin.Utils.DateTimeToUnixTime(DateTimeOffset.UtcNow).ToString());
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			disposable?.Dispose();
			foreach (var indexer in Indexers.All())
				EnsureRPCReadyFileDeleted(indexer.Network);
			return Task.CompletedTask;
		}

		private void EnsureRPCReadyFileDeleted(NBXplorerNetwork network)
		{
			var file = GetFilePath(network);
			if (File.Exists(file))
				File.Delete(file);
		}

		private string GetFilePath(NBXplorerNetwork network)
		{
			return Path.Combine(ExplorerConfiguration.SignalFilesDir, $"{network.CryptoCode.ToLowerInvariant()}_fully_synched");
		}
	}
}
