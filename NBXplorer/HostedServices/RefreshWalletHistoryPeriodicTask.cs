using Dapper;
using NBXplorer.Backends.Postgres;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer.HostedServices
{
	public class RefreshWalletHistoryPeriodicTask : IPeriodicTask
	{
		public RefreshWalletHistoryPeriodicTask(DbConnectionFactory connectionFactory)
		{
			ConnectionFactory = connectionFactory;
		}

		public DbConnectionFactory ConnectionFactory { get; }

		public async Task Do(CancellationToken cancellationToken)
		{
			await using var conn = await ConnectionFactory.CreateConnection(b =>
			{
				b.CommandTimeout = Constants.FifteenMinutes;
			});
			await conn.ExecuteAsync("SELECT wallets_history_refresh();");
		}
	}
}
