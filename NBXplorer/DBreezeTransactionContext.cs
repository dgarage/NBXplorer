using DBreeze;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class DBreezeTransactionContext
	{
		DBreezeEngine _Engine;
		DBreeze.Transactions.Transaction _Tx;
		Thread _Loop;
		readonly BlockingCollection<(Action, TaskCompletionSource<object>)> _Actions = new BlockingCollection<(Action, TaskCompletionSource<object>)>(new ConcurrentQueue<(Action, TaskCompletionSource<object>)>());
		TaskCompletionSource<bool> _Done;
		CancellationTokenSource _Cancel;
		bool _IsDisposed;
		bool _IsStarted;
		public DBreezeTransactionContext(DBreezeEngine engine)
		{
			if (engine == null)
				throw new ArgumentNullException(nameof(engine));
			_Engine = engine;
		}

		public async Task StartAsync()
		{
			if (_IsDisposed)
				throw new ObjectDisposedException(nameof(DBreezeTransactionContext));
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
		public event Action<DBreezeTransactionContext, Exception> UnhandledException;
		void Loop()
		{
			try
			{
				foreach (var act in _Actions.GetConsumingEnumerable(_Cancel.Token))
				{
					try
					{
						act.Item1();
						// The action is setting the result, so no need of TrySetResult here
					}
					catch (OperationCanceledException ex) when (_Cancel.IsCancellationRequested)
					{
						act.Item2.TrySetException(ex);
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

		public Task DoAsync(Action<DBreeze.Transactions.Transaction> action)
		{
			if (_IsDisposed)
				throw new ObjectDisposedException(nameof(DBreezeTransactionContext));
			return DoAsyncCore(action);
		}
		public Task<T> DoAsync<T>(Func<DBreeze.Transactions.Transaction, T> action)
		{
			if (_IsDisposed)
				throw new ObjectDisposedException(nameof(DBreezeTransactionContext));
			return DoAsyncCore(action);
		}

		private Task DoAsyncCore(Action<DBreeze.Transactions.Transaction> action)
		{
			var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
			_Actions.Add((() => { action(_Tx); completion.TrySetResult(true); }, completion));
			return completion.Task;
		}
		private async Task<T> DoAsyncCore<T>(Func<DBreeze.Transactions.Transaction, T> action)
		{
			var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
			_Actions.Add((() => { completion.TrySetResult(action(_Tx)); }, completion));
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
				await DoAsyncCore(tx => { tx.Dispose(); });
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
