using System;
using System.Collections.Generic;

namespace NBXplorer
{
	public class CompositeDisposable : IDisposable
	{
		List<IDisposable> _Disposables = new List<IDisposable>();
		public void Add(IDisposable disposable)
		{
			_Disposables.Add(disposable);
		}
		public void Dispose()
		{
			foreach(var d in _Disposables)
				d.Dispose();
			_Disposables.Clear();
		}
	}
}
