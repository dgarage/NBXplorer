using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer
{
	public abstract class NotificationSessionBase
	{
		public NewEventBase NextEvent(CancellationToken cancellation = default)
		{
			return NextEventAsync(cancellation).GetAwaiter().GetResult();
		}
		public abstract Task<NewEventBase> NextEventAsync(CancellationToken cancellation = default);
	}
}
	