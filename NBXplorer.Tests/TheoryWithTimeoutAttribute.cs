using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
