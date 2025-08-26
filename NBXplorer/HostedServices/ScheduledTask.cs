using System;

namespace NBXplorer.HostedServices
{
	public class ScheduledTask
	{
		public ScheduledTask(Type periodicTypeTask, TimeSpan every)
		{
			PeriodicTaskType = periodicTypeTask;
			Every = every;
		}
		public Type PeriodicTaskType { get; set; }
		public TimeSpan Every { get; set; } = TimeSpan.FromMinutes(5.0);
	}
}
