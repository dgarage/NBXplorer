using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit.Abstractions;

namespace NBXplorer.Tests
{
	public class TestOutputHelperFactory : ILoggerFactory, ILogger
	{
		public void AddProvider(ILoggerProvider provider)
		{
			
		}
		ITestOutputHelper _Out;
		public TestOutputHelperFactory(ITestOutputHelper output)
		{
			_Out = output;
		}

		public IDisposable BeginScope<TState>(TState state)
		{
			return this;
		}

		public ILogger CreateLogger(string categoryName)
		{
			return this;
		}

		public void Dispose()
		{
			
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return true;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
		{
			_Out.WriteLine(formatter(state, exception));
		}
	}
}
