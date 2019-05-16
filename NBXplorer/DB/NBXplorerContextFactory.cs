using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBXplorer.Logging;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NBXplorer.DB
{
	public class NBXplorerContextFactory
	{
		string _ConnectionString;
		public NBXplorerContextFactory(string connectionString, IApplicationLifetime applicationLifetime)
		{
			_ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
			ApplicationLifetime = applicationLifetime;
		}
		CancellationTokenSource _Cts = new CancellationTokenSource();
		List<NpgsqlConnection> _PosgresConnections = new List<NpgsqlConnection>();

		int _MaxConnectionCount = 10;
		int _ConnectionCreated = 0;
		public async Task<NBXplorerDBContext> GetContext()
		{
		retry:
			if (!_Available.Reader.TryRead(out var connection))
			{
				if (_ConnectionCreated <= _MaxConnectionCount &&
					Interlocked.Increment(ref _ConnectionCreated) <= _MaxConnectionCount)
				{
					connection = await OpenConnection();
				}
				else
				{
					try
					{
						await _Available.Reader.WaitToReadAsync(_Cts.Token);
					}
					catch when (_Cts.IsCancellationRequested)
					{
						throw new ObjectDisposedException(nameof(NBXplorerContextFactory));
					}
					goto retry;
				}
			}
			if (connection.State != System.Data.ConnectionState.Open)
			{
				lock (_PosgresConnections)
				{
					_PosgresConnections.Remove(connection);
					Interlocked.Decrement(ref _ConnectionCreated);
				}
				await Task.Delay(100);
				goto retry;
			}
			return new NBXplorerDBContext(this, connection);
		}

		private async Task<NpgsqlConnection> OpenConnection()
		{
			NpgsqlConnection connection = new NpgsqlConnection(_ConnectionString);
			try
			{
				await connection.OpenAsync(_Cts.Token);
			}
			catch (Exception ex)
			{
				Logs.Explorer.LogError(ex, "Error while trying to open connection to the database, stopping NBXplorer...");
				ApplicationLifetime?.StopApplication();
				throw;
			}
			lock (_PosgresConnections)
			{
				_PosgresConnections.Add(connection);
			}
			return connection;
		}

		Channel<NpgsqlConnection> _Available = Channel.CreateUnbounded<NpgsqlConnection>();

		public IApplicationLifetime ApplicationLifetime { get; }

		internal void Return(NpgsqlConnection connection)
		{
			_Available.Writer.TryWrite(connection);
		}

		public void Migrate()
		{
		retry:
			var connString = new NpgsqlConnectionStringBuilder(_ConnectionString);
			using (var connection = new NpgsqlConnection(connString.ConnectionString))
			{
				try
				{
					connection.Open();
				}
				catch (PostgresException ex) when (ex.SqlState == "3D000")
				{
					var oldDB = connString.Database;
					connString.Database = null;
					using (var createDBConnect = new NpgsqlConnection(connString.ConnectionString))
					{
						try
						{
							createDBConnect.Open();
						
							var createDB = createDBConnect.CreateCommand();
							// We need LC_CTYPE set to C to get proper indexing on the columns when making
							// partial pattern queries on the primary key (LIKE operator)
							createDB.CommandText = $"CREATE DATABASE \"{oldDB}\" " +
								$"LC_COLLATE = 'C' " +
								$"TEMPLATE=template0 " +
								$"LC_CTYPE = 'C' " +
								$"ENCODING = 'UTF8'";
							createDB.ExecuteNonQuery();
							connection.Open();
						}
						catch (PostgresException ex2) when (ex2.SqlState == "3D000" || ex2.SqlState == "42501")
						{
							Logs.Explorer.LogCritical($"The database {connString.Database} does not exists, and this user does not have enough priviledge to create it.");
							throw;
						}
					}
				}
				catch (PostgresException ex) when (ex.SqlState == "57P03")
				{
					Logs.Explorer.LogInformation("Database starting retrying soon...");
					Thread.Sleep(5000);
					goto retry;
				}

				var command = connection.CreateCommand();
				command.CommandText = String.Join(";", new[]
				{
					$"CREATE TABLE IF NOT EXISTS \"GenericTables\" (\"PartitionKeyRowKey\" text PRIMARY KEY, \"Value\" bytea, \"DeletedAt\" timestamp)",
					"CREATE TABLE IF NOT EXISTS \"Events\" ( \"id\" BIGSERIAL PRIMARY KEY, \"data\" bytea NOT NULL, \"event_id\" VARCHAR(40) UNIQUE)",
					"CREATE OR REPLACE FUNCTION insert_event(\"data_arg\" bytea, \"event_id_arg\" VARCHAR(40)) RETURNS BIGINT AS $$\r\nDECLARE\r\n\t\"inserted_id\" BIGINT;\r\nBEGIN\r\n\tPERFORM pg_advisory_xact_lock(183620);\r\n\tINSERT INTO \"Events\" (\"data\", \"event_id\") VALUES (\"data_arg\", \"event_id_arg\") \r\n\t\tRETURNING \"id\" INTO \"inserted_id\";\r\n\tRETURN \"inserted_id\";\r\nEXCEPTION  WHEN unique_violation THEN\r\n\tSELECT \"id\" FROM \"Events\" WHERE \"event_id\" = \"event_id_arg\" INTO \"inserted_id\";\r\n\tRETURN \"inserted_id\";\r\nEND;\r\n$$ LANGUAGE PLpgSQL;\r\n"
				});
				try
				{
					command.ExecuteNonQuery();
				}
				catch (PostgresException ex) when (ex.SqlState == "42501")
				{
					Logs.Explorer.LogCritical("Impossible to create the schema, the user does not have permission");
					throw;
				}

			}
		}

		public async Task DisposeAsync()
		{
			_Cts.Cancel();

			while (_PosgresConnections.Count != 0 && await _Available.Reader.WaitToReadAsync())
			{
				if (_Available.Reader.TryRead(out var connection))
				{
					connection.Close();
					lock (_PosgresConnections)
					{
						_PosgresConnections.Remove(connection);
						Interlocked.Decrement(ref _ConnectionCreated);
					}
				}
			}
		}

		public void ConfigureBuilder(DbContextOptionsBuilder builder)
		{
			builder.UseNpgsql(_ConnectionString);
		}
	}
}
