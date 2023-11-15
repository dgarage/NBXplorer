using Dapper;
using NBXplorer.Backends.Postgres;
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
			_DS = ConnectionFactory.CreateDataSourceBuilder(b => b.CommandTimeout = Constants.FifteenMinutes).Build();
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
