using Xunit.Abstractions;

namespace NBXplorer.Tests;

public class TesterLogs(ITestOutputHelper helper)
{
	public XUnitLog Tester { get; } = new XUnitLog(helper) { Name = "Tests" };
	public XUnitLoggerProvider LogProvider { get; } = new(helper);
}