using NBitcoin;
using Microsoft.Extensions.Logging;
using Completion = System.Threading.Tasks.TaskCompletionSource<bool>;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBXplorer.Models;
using NBXplorer.Logging;

namespace NBXplorer
{
    public class ChainEvents
    {
		public async Task WaitFor(DerivationStrategyBase pubKey, CancellationToken cancellation = default(CancellationToken))
		{
			TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();

			var key = pubKey.GetHash();

			lock(_WaitFor)
			{
				_WaitFor.Add(key, completion);
			}

			cancellation.Register(() =>
			{
				completion.TrySetCanceled();
			});

			try
			{
				await completion.Task;
			}
			finally
			{
				lock(_WaitFor)
				{
					_WaitFor.Remove(key, completion);
				}
			}
		}

		public void Notify(TransactionMatch match, bool log)
		{
			if(log)
				Logs.Explorer.LogInformation($"A wallet received money");
			var key = match.DerivationStrategy.GetHash();
			lock(_WaitFor)
			{
				IReadOnlyCollection<Completion> completions;
				if(_WaitFor.TryGetValue(key, out completions))
				{
					foreach(var completion in completions.ToList())
					{
						completion.TrySetResult(true);
					}
				}
			}
		}

		MultiValueDictionary<uint160, Completion> _WaitFor = new MultiValueDictionary<uint160, Completion>();
	}
}
