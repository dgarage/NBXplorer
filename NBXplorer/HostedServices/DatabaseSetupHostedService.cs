using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBXplorer.Backend;
using Npgsql;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer.HostedServices
{
	public class DatabaseSetupHostedService : IHostedService
	{
		public DatabaseSetupHostedService(ILoggerFactory loggerFactory,  DbConnectionFactory connectionFactory)
		{
			Logger = loggerFactory.CreateLogger("NBXplorer.DatabaseSetup");
			ConnectionFactory = connectionFactory;
		}

		public ILogger Logger { get; }
		public DbConnectionFactory ConnectionFactory { get; }

		public async Task StartAsync(CancellationToken cancellationToken)
		{
			Logger.LogInformation("Postgres services activated");
			var dsBuilder = ConnectionFactory.CreateDataSourceBuilder(b => b.CommandTimeout = Constants.TenHours);
			retry:
			try
			{
				await using var ds = dsBuilder.Build();
				using var conn = await ds.ReliableOpenConnectionAsync();
				await RunScripts(conn);
			}
			catch (Npgsql.NpgsqlException pgex) when (pgex.SqlState == PostgresErrorCodes.InvalidCatalogName)
			{
				string dbname = string.Empty;
				await using var ds = ConnectionFactory.CreateDataSourceBuilder(b =>
				{
					dbname = b.Database;
					Logger.LogInformation($"Database '{dbname}' doesn't exists, creating it...");
					b.Database = null;
				}).Build();
				using var conn = await ds.ReliableOpenConnectionAsync();
				await conn.ExecuteAsync($"CREATE DATABASE {dbname} TEMPLATE 'template0' LC_CTYPE 'C' LC_COLLATE 'C' ENCODING 'UTF8'");
				goto retry;
			}
		}

		private async Task RunScripts(NpgsqlConnection conn)
		{
			await using (conn)
			{
				if (conn.PostgreSqlVersion.Major <= 11)
					Logger.LogWarning($"You are using postgres {conn.PostgreSqlVersion.Major}, this major release reached end-of-life (EOL) and is no longer supported, use at your own risks. (https://www.postgresql.org/support/versioning/)");

				HashSet<string> executed;
				try
				{
					executed = (await conn.QueryAsync<string>("SELECT script_name FROM nbxv1_migrations")).ToHashSet();
				}
				catch (Npgsql.NpgsqlException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
				{
					executed = new HashSet<string>();
				}
				foreach (var resource in System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceNames()
																 .Where(n => n.EndsWith(".sql", System.StringComparison.InvariantCulture))
																 .OrderBy(n => n))
				{
					var parts = resource.Split('.');
					if (!int.TryParse(parts[^3], NumberStyles.Any, CultureInfo.InvariantCulture, out _))
						continue;
					var scriptName = $"{parts[^3]}.{parts[^2]}";
					if (executed.Contains(scriptName))
						continue;
					if (scriptName == "018.FastWalletRecent" && conn.PostgreSqlVersion.Major <= 11)
					{
						Logger.LogWarning($"Skipping script {scriptName} because it is not supported by postgres {conn.PostgreSqlVersion.Major}");
						continue;
					}

					var stream = System.Reflection.Assembly.GetExecutingAssembly()
														   .GetManifestResourceStream(resource);
					string content = null;
					using (var reader = new StreamReader(stream, Encoding.UTF8))
					{
						content = reader.ReadToEnd();
					}
					Logger.LogInformation($"Execute script {scriptName}...");
					await conn.ExecuteAsync($"{content}; INSERT INTO nbxv1_migrations VALUES (@scriptName)", new { scriptName });
				}
				await conn.ReloadTypesAsync();
			}
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			Npgsql.NpgsqlConnection.ClearAllPools();
			return Task.CompletedTask;
		}
	}
}
