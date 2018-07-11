using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NBXplorer.DB
{
	public class NBXplorerContextFactory : IDisposable
	{
		string _ConnectionString;
		public NBXplorerContextFactory(string connectionString)
		{
			_ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}
		CancellationTokenSource _Cts = new CancellationTokenSource();
		List<NpgsqlConnection> _PosgresConnections = new List<NpgsqlConnection>();

		int _MaxConnectionCount = 10;
		int _ConnectionCreated = 0;
		public async Task<NBXplorerDBContext> GetContext()
		{
			if(!_Available.Reader.TryRead(out var connection))
			{
				while(true)
				{
					if(_ConnectionCreated <= _MaxConnectionCount &&
						Interlocked.Increment(ref _ConnectionCreated) <= _MaxConnectionCount)
					{
						connection = await OpenConnection();
					}
					else
					{
						connection = await _Available.Reader.ReadAsync(_Cts.Token);
						if(connection.State != System.Data.ConnectionState.Open)
						{
							lock(_PosgresConnections)
							{
								_PosgresConnections.Remove(connection);
								Interlocked.Decrement(ref _ConnectionCreated);
							}
							continue;
						}
					}
					break;
				}
			}
			return new NBXplorerDBContext(this, connection);
		}

		private async Task<NpgsqlConnection> OpenConnection()
		{
			NpgsqlConnection connection = new NpgsqlConnection(_ConnectionString);
			await connection.OpenAsync(_Cts.Token);
			lock(_PosgresConnections)
			{
				_PosgresConnections.Add(connection);
			}
			return connection;
		}

		Channel<NpgsqlConnection> _Available = Channel.CreateUnbounded<NpgsqlConnection>();

		internal void Return(NpgsqlConnection connection)
		{
			_Available.Writer.TryWrite(connection);
		}

		public void Migrate()
		{
			var connString = new NpgsqlConnectionStringBuilder(_ConnectionString);
			using(var connection = new NpgsqlConnection(connString.ConnectionString))
			{
				try
				{
					connection.Open();
				}
				catch(PostgresException ex) when(ex.SqlState == "3D000")
				{
					var oldDB = connString.Database;
					connString.Database = null;
					using(var createDBConnect = new NpgsqlConnection(connString.ConnectionString))
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
				}
				var command = connection.CreateCommand();
				command.CommandText = $"CREATE TABLE IF NOT EXISTS \"GenericTables\" (\"PartitionKeyRowKey\" text PRIMARY KEY, \"Value\" bytea, \"DeletedAt\" timestamp)";
				command.ExecuteNonQuery();
			}
		}

		public void Dispose()
		{
			_Cts.Cancel();
			lock(_PosgresConnections)
			{
				foreach(var connection in _PosgresConnections)
					connection.Close();
			}
		}

		public void ConfigureBuilder(DbContextOptionsBuilder builder)
		{
			builder.UseNpgsql(_ConnectionString);
		}
	}
}
