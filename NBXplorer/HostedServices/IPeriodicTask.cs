using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer.HostedServices
{
	public interface IPeriodicTask
	{
		Task Do(CancellationToken cancellationToken);
	}
}
