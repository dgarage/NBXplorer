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
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
	public class FactWithTimeoutAttribute : FactAttribute
	{
		public FactWithTimeoutAttribute()
		{
			Timeout = 60_000;
		}
	}
}
