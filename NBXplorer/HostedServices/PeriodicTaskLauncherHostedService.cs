using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NBitcoin.Logging;
using Logs = NBXplorer.Logging.Logs;

namespace NBXplorer.HostedServices
{
	public class PeriodicTaskLauncherHostedService : IHostedService
	{
		public PeriodicTaskLauncherHostedService(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
		{
			ServiceProvider = serviceProvider;
			Logger = loggerFactory.CreateLogger("NBXplorer.PeriodicTasks");
		}

		public IServiceProvider ServiceProvider { get; }
		public ILogger Logger { get; }

		Channel<ScheduledTask> jobs = Channel.CreateBounded<ScheduledTask>(100);
		CancellationTokenSource cts;
		public Task StartAsync(CancellationToken cancellationToken)
		{
			cts = new CancellationTokenSource();
			foreach (var task in ServiceProvider.GetServices<ScheduledTask>())
				jobs.Writer.TryWrite(task);

			loop = Task.WhenAll(Enumerable.Range(0, 3).Select(i => Loop(cts.Token, i)).ToArray());
			return Task.CompletedTask;
		}
		Task loop;
		private async Task Loop(CancellationToken token, int i)
		{
			try
			{
				Logs.Explorer.LogInformation($"Starting loop {i}");
				await foreach (var job in jobs.Reader.ReadAllAsync(token))
				{
					Logs.Explorer.LogInformation($"{i}: Run job {job.PeriodicTaskType}");
					if (job.NextScheduled <= DateTimeOffset.UtcNow)
					{
						Logs.Explorer.LogInformation($"{i}: GO! {job.PeriodicTaskType}");
						var t = (IPeriodicTask)ServiceProvider.GetService(job.PeriodicTaskType);
						try
						{
							await t.Do(token);
						}
						catch when (token.IsCancellationRequested)
						{
							throw;
						}
						catch (Exception ex)
						{
							Logger.LogError(ex, $"Unhandled error in job {job.PeriodicTaskType.Name}");
						}
						finally
						{
							job.NextScheduled = DateTimeOffset.UtcNow + job.Every;
							Logs.Explorer.LogInformation($"{i}: Rescheduled for {job.NextScheduled:u}");
						}
					}
					_ = Wait(job, token);
					Logs.Explorer.LogInformation($"{i}: NEEXT");
				}
			}
			catch when (token.IsCancellationRequested)
			{
			}
		}

		private async Task Wait(ScheduledTask job, CancellationToken token)
		{
			var timeToWait = job.NextScheduled - DateTimeOffset.UtcNow;
			try
			{
				Logs.Explorer.LogInformation($"Wait for {job.PeriodicTaskType} for {timeToWait.TotalMinutes} minutes");
				await Task.Delay(timeToWait, token);
			}
			catch { }
			Logs.Explorer.LogInformation($"Wait to write");
			while (await jobs.Writer.WaitToWriteAsync())
			{
				Logs.Explorer.LogInformation($"Writing");
				if (jobs.Writer.TryWrite(job))
				{
					Logs.Explorer.LogInformation($"Write!");
					break;
				}
			}
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			cts?.Cancel();
			jobs.Writer.TryComplete();
			if (loop is not null)
				await loop;
		}
	}
}
