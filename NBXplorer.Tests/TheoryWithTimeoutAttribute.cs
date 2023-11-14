using System;
using Xunit;

namespace NBXplorer.Tests
{
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
	public class TheoryWithTimeoutAttribute : TheoryAttribute
	{
		public TheoryWithTimeoutAttribute()
		{
			Timeout = 60_000;
		}
	}
}
