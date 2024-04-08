using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NBXplorer.Configuration;
using Npgsql;
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace NBXplorer.Backends
{
	public class DbConnectionFactory : IAsyncDisposable
	{
		public DbConnectionFactory(ILogger<DbConnectionFactory> logger,
			IConfiguration configuration,
			ExplorerConfiguration conf,
			KeyPathTemplates keyPathTemplates)
		{
			Logger = logger;
			ExplorerConfiguration = conf;
			KeyPathTemplates = keyPathTemplates;
			ConnectionString = configuration.GetRequired("POSTGRES");
			_DS = CreateDataSourceBuilder(null).Build();
		}

		public NpgsqlDataSourceBuilder CreateDataSourceBuilder(Action<NpgsqlConnectionStringBuilder> action)
		{
			var connStrBuilder = new NpgsqlConnectionStringBuilder(ConnectionString);
			// Since we create lots of connection in the indexer loop, this saves one round
			// trip.
			connStrBuilder.NoResetOnClose = true;
			// This force connections to recreate, fixing some issues where connection
			// take more and more RAM on postgres.
			connStrBuilder.ConnectionLifetime = (int)TimeSpan.FromMinutes(10).TotalSeconds;
			action?.Invoke(connStrBuilder);
			var builder = new NpgsqlDataSourceBuilder(connStrBuilder.ConnectionString);
			DbConnectionHelper.Register(builder);
			builder.Build();
			return builder;
		}

		NpgsqlDataSource _DS;
		public string ConnectionString { get; }
		public ILogger<DbConnectionFactory> Logger { get; }
		public ExplorerConfiguration ExplorerConfiguration { get; }
		public KeyPathTemplates KeyPathTemplates { get; }

		public async Task<DbConnectionHelper> CreateConnectionHelper(NBXplorerNetwork network)
		{
			return new DbConnectionHelper(network, await CreateConnection(), KeyPathTemplates)
			{
				MinPoolSize = ExplorerConfiguration.MinGapSize,
				MaxPoolSize = ExplorerConfiguration.MaxGapSize
			};
		}

		public async Task<DbConnection> CreateConnection(CancellationToken cancellationToken = default)
		{
			return await _DS.ReliableOpenConnectionAsync(cancellationToken);
		}

		public ValueTask DisposeAsync()
		{
			return _DS.DisposeAsync();
		}
	}
}
