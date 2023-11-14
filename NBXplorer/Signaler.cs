using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class Signaler
	{
		Channel<bool> _Channel = Channel.CreateUnbounded<bool>();
		public void Set()
		{
			_Channel.Writer.TryWrite(true);
		}

		public async Task<bool> Wait(TimeSpan timeout, CancellationToken cancellationToken = default)
		{
			using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
			{
				cancel.CancelAfter(timeout);
				try
				{
					await Wait(cancel.Token);
				}
				catch when (!cancellationToken.IsCancellationRequested) { return false; }
			}
			return true;
		}

		public async Task Wait(CancellationToken cancellationToken = default)
		{
			if (await _Channel.Reader.WaitToReadAsync(cancellationToken))
			{
				cancellationToken.ThrowIfCancellationRequested();
				while(_Channel.Reader.TryRead(out _))
				{

				}
			}
		}
	}
}
