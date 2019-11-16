using DBriize;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class DBriizeTransactionContext
	{
		DBriizeEngine _Engine;
		DBriize.Transactions.Transaction _Tx;
		Thread _Loop;
		readonly BlockingCollection<(Action<DBriize.Transactions.Transaction>, TaskCompletionSource<object>, CancellationToken)> _Actions = new BlockingCollection<(Action<DBriize.Transactions.Transaction>, TaskCompletionSource<object>, CancellationToken)>(new ConcurrentQueue<(Action<DBriize.Transactions.Transaction>, TaskCompletionSource<object>, CancellationToken)>());
		TaskCompletionSource<bool> _Done;
		CancellationTokenSource _Cancel;
		bool _IsDisposed;
		bool _IsStarted;
		public DBriizeTransactionContext(DBriizeEngine engine)
		{
			if (engine == null)
				throw new ArgumentNullException(nameof(engine));
			_Engine = engine;
		}

		public async Task StartAsync()
		{
			if (_IsDisposed)
				throw new ObjectDisposedException(nameof(DBriizeTransactionContext));
			if (_IsStarted)
				return;
			_IsStarted = true;
			_Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			_Cancel = new CancellationTokenSource();
			_Loop = new Thread(Loop)
			{
				IsBackground = false
			};
			_Loop.Start();
			await DoAsync((tx) => { _Tx = _Engine.GetTransaction(); });
		}
		public event Action<DBriizeTransactionContext, Exception> UnhandledException;
		void Loop()
		{
			try
			{
				bool initialized = false;
				foreach (var act in _Actions.GetConsumingEnumerable(_Cancel.Token))
				{
					if (act.Item3.IsCancellationRequested)
					{
						act.Item2.TrySetCanceled();
						continue;
					}
					try
					{
						if (!initialized)
						{
							act.Item1(null);
							initialized = true;
							AssertTxIsSet();
						}
						else
						{
							AssertTxIsSet();
							act.Item1(_Tx);
						}
						// The action is setting the result, so no need of TrySetResult here
					}
					catch (OperationCanceledException) when (_Cancel.IsCancellationRequested)
					{
						act.Item2.TrySetCanceled();
						break;
					}
					catch (Exception ex)
					{
						UnhandledException?.Invoke(this, ex);
						act.Item2.TrySetException(ex);
					}
				}
			}
			catch (OperationCanceledException) when (_Cancel.IsCancellationRequested) { }
			catch (Exception ex)
			{
				UnhandledException?.Invoke(this, ex);
			}
			_Done.TrySetResult(true);
		}

		private void AssertTxIsSet()
		{
			if (_Tx == null)
				throw new InvalidOperationException("Bug in NBXplorer. _Tx should be set by now, report on github.");
		}

		public Task DoAsync(Action<DBriize.Transactions.Transaction> action, CancellationToken cancellation = default)
		{
			if (_IsDisposed)
				throw new ObjectDisposedException(nameof(DBriizeTransactionContext));
			return DoAsyncCore(action, cancellation);
		}
		public Task<T> DoAsync<T>(Func<DBriize.Transactions.Transaction, T> action, CancellationToken cancellation = default)
		{
			if (_IsDisposed)
				throw new ObjectDisposedException(nameof(DBriizeTransactionContext));
			return DoAsyncCore(action, cancellation);
		}

		private Task DoAsyncCore(Action<DBriize.Transactions.Transaction> action, CancellationToken cancellation)
		{
			var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
			_Actions.Add(((tx) => { action(tx); completion.TrySetResult(true); }, completion, cancellation));
			return completion.Task;
		}
		private async Task<T> DoAsyncCore<T>(Func<DBriize.Transactions.Transaction, T> action, CancellationToken cancellation)
		{
			var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
			_Actions.Add(((tx) => { completion.TrySetResult(action(tx)); }, completion, cancellation));
			return (T)(await completion.Task);
		}

		public async Task DisposeAsync()
		{
			if (_IsDisposed)
				return;
			_IsDisposed = true;
			try
			{
				if (!_IsStarted)
					return;
				await DoAsyncCore(tx => { tx.Dispose(); }, default);
				_Cancel.Cancel();
				await _Done.Task;
			}
			catch
			{
			}
			finally
			{
				CancelPendingTasks();
			}
		}

		private void CancelPendingTasks()
		{
			foreach (var action in _Actions)
			{
				try
				{
					action.Item2.TrySetCanceled();
				}
				catch { }
			}
		}
	}
}
