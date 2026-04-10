using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace NBXplorer.Tests;

public class UnitTestBase(ITestOutputHelper helper)
{
	public TesterLogs Logs { get; set; } = new TesterLogs(helper);

	public ServerTester CreateTester([CallerMemberName] string caller = null) => ServerTester.Create(Logs, caller);
	public ServerTester CreateTesterNoAutoStart([CallerMemberName] string caller = null) => ServerTester.CreateNoAutoStart(Logs, caller);
}