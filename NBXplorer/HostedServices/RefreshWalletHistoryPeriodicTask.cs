using Dapper;
using NBXplorer.Backend;
using Npgsql;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer.HostedServices
{
	public class RefreshWalletHistoryPeriodicTask : IPeriodicTask, IAsyncDisposable
	{
		public RefreshWalletHistoryPeriodicTask(DbConnectionFactory connectionFactory)
		{
			ConnectionFactory = connectionFactory;
			_DS = ConnectionFactory.CreateDataSourceBuilder(b =>
			{
				b.CommandTimeout = Constants.FifteenMinutes;
				// Only running one command every 15min, and it can takes some times
				b.Pooling = false;
			}).Build();
		}

		public DbConnectionFactory ConnectionFactory { get; }

		private readonly NpgsqlDataSource _DS;

		public async Task Do(CancellationToken cancellationToken)
		{
			await using var conn = await _DS.ReliableOpenConnectionAsync();
			await conn.ExecuteAsync("SELECT wallets_history_refresh();");
		}

		public ValueTask DisposeAsync()
		{
			return _DS.DisposeAsync();
		}
	}
}
