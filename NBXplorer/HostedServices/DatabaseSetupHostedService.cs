using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBXplorer.Backends.Postgres;
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
			retry:
			try
			{
				await using var conn = await ConnectionFactory.CreateConnection(b =>
				{
					b.Timeout = 120;
				});
				await RunScripts(conn);
			}
			catch (Npgsql.NpgsqlException pgex) when (pgex.SqlState == "3D000")
			{
				var builder = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionFactory.ConnectionString);
				var dbname = builder.Database;
				Logger.LogInformation($"Database '{dbname}' doesn't exists, creating it...");
				builder.Database = null;
				var conn2Str = builder.ToString();
				var conn2 = new Npgsql.NpgsqlConnection(conn2Str);
				await conn2.OpenAsync();
				await conn2.ExecuteAsync($"CREATE DATABASE {dbname} TEMPLATE 'template0' LC_CTYPE 'C' LC_COLLATE 'C' ENCODING 'UTF8'");
				goto retry;
			}
		}

		private async Task RunScripts(System.Data.Common.DbConnection conn)
		{
			DbConnectionHelper.Register(NpgsqlConnection.GlobalTypeMapper);
			await using (conn)
			{
				HashSet<string> executed;
				try
				{
					executed = (await conn.QueryAsync<string>("SELECT script_name FROM nbxv1_migrations")).ToHashSet();
				}
				catch (Npgsql.NpgsqlException ex) when (ex.SqlState == "42P01")
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
				((NpgsqlConnection)conn).ReloadTypes();
			}
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			Npgsql.NpgsqlConnection.ClearAllPools();
			return Task.CompletedTask;
		}
	}
}
