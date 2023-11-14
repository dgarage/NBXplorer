using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;

namespace NBXplorer.Backends
{
	public interface IRepositoryProvider : IHostedService
	{
		Task StartCompletion { get; }

		IRepository GetRepository(NBXplorerNetwork network);
		IRepository GetRepository(string cryptoCode);
	}
}