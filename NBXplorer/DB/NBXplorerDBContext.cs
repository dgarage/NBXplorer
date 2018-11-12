using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using System.Data.Common;
using System.Text;

namespace NBXplorer.DB
{
	public class GenericRow<TValue>
	{
		public bool Exists
		{
			get
			{
				return true;
			}
		}

		public string Key
		{
			get; set;
		}

		TValue _Value;
		bool _Set = false;
		public TValue Value
		{
			get
			{
				if (!_Set && FetchValue != null)
				{
					_Value = FetchValue().GetAwaiter().GetResult();
				}
				return _Value;
			}
			set
			{
				_Set = true;
				_Value = value;
			}
		}

		internal Func<Task<TValue>> FetchValue
		{
			get;
			set;
		}
	}

	public class NBXplorerDBContext : IDisposable
	{
		NBXplorerContextFactory _Parent;
		public NBXplorerDBContext(NBXplorerContextFactory parent, NpgsqlConnection connection)
		{
			_Parent = parent;
			Connection = connection;
		}

		public NpgsqlConnection Connection
		{
			get; set;
		}

		public bool ValuesLazyLoadingIsOn
		{
			get; set;
		} = true;

		internal void RemoveKey(string partitionKey, string rowKey)
		{
			var partitionKeyRowKey = PartitionKeyRowKey(partitionKey, rowKey);
			var query = $"UPDATE \"GenericTables\" " +
						$"SET \"DeletedAt\" = now() " +
						$"WHERE \"PartitionKeyRowKey\" = @partitionKeyRowKey{_ToCommit.Count} AND \"DeletedAt\" IS NULL";
			if (ForceDelete)
				query = $"DELETE FROM \"GenericTables\" " +
						$"WHERE \"PartitionKeyRowKey\" = @partitionKeyRowKey{_ToCommit.Count};";
			var partitionKeyRowKeyParam = new NpgsqlParameter($"partitionKeyRowKey{_ToCommit.Count}", partitionKeyRowKey);
			_ToCommit.Add((query, new DbParameter[] { partitionKeyRowKeyParam }));
		}

		List<(string, DbParameter[])> _ToCommit = new List<(string, DbParameter[])>();

		internal void Insert(string partitionKey, string rowKey, byte[] data)
		{
			var partitionKeyRowKeyParam = new NpgsqlParameter($"partitionKeyRowKey{_ToCommit.Count}", PartitionKeyRowKey(partitionKey, rowKey));
			var valueParam = new NpgsqlParameter($"value{_ToCommit.Count}", data);

			var deletedAt = ForceDelete ? ", \"DeletedAt\" = NULL" : "WHERE \"GenericTables\".\"DeletedAt\" IS NULL";

			_ToCommit.Add(($"INSERT INTO \"GenericTables\" ( \"PartitionKeyRowKey\", \"Value\") " +
						   $"VALUES (@partitionKeyRowKey{_ToCommit.Count}, @value{_ToCommit.Count}) " +
						   $"ON CONFLICT ( \"PartitionKeyRowKey\") DO UPDATE SET \"Value\" = @value{_ToCommit.Count} {deletedAt}", new DbParameter[] { partitionKeyRowKeyParam, valueParam }));
		}

		internal async Task<bool> TakeLock(string partitionKey, string rowKey)
		{
			var partitionKeyRowKey = PartitionKeyRowKey(partitionKey, rowKey);
			var valueParam = new NpgsqlParameter($"value", new byte[0]);
			using (var command = Connection.CreateCommand())
			{
				var partitionKeyParam = new NpgsqlParameter("partitionKeyRowKey", partitionKeyRowKey);
				command.Parameters.Add(partitionKeyParam);
				command.Parameters.Add(valueParam);
				command.CommandText = "INSERT INTO \"GenericTables\" ( \"PartitionKeyRowKey\", \"Value\", \"DeletedAt\") " +
									  "VALUES (@partitionKeyRowKey, @value, now() + interval '5 minutes') " +
									  "ON CONFLICT ( \"PartitionKeyRowKey\") DO UPDATE SET \"DeletedAt\" = (now() + interval '5 minutes') " +
									  "WHERE (\"GenericTables\".\"PartitionKeyRowKey\" = @partitionKeyRowKey) AND (\"GenericTables\".\"DeletedAt\" IS NULL OR \"GenericTables\".\"DeletedAt\" < now());";
				return await command.ExecuteNonQueryAsync() == 1;
			}
		}

		private string PartitionKeyRowKey(string partitionKey, string rowKey)
		{
			Validate(partitionKey, rowKey);
			return $"{partitionKey}@@{rowKey}";
		}

		private static void Validate(string partitionKey, string rowKey)
		{
			if (partitionKey.Contains("@@", StringComparison.OrdinalIgnoreCase) || rowKey.Contains("@@", StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("PartitionKey or RowKey should not contains '@@'");
		}

		internal void Insert<T>(string partitionKey, string rowKey, T value)
		{
			if (value is byte[] b)
				Insert(partitionKey, rowKey, b);
			else if (value is int i)
				Insert(partitionKey, rowKey, NBitcoin.Utils.ToBytes((uint)i, true));
		}

		internal async Task<GenericRow<TValue>> Select<TValue>(string partitionKey, string rowKey)
		{
			var partitionKeyRowKey = PartitionKeyRowKey(partitionKey, rowKey);
			var query = $"SELECT {Columns} FROM \"GenericTables\" " +
						$"WHERE \"PartitionKeyRowKey\" = @partitionKeyRowKey AND \"DeletedAt\" IS NULL " +
						$"LIMIT 1";
			var partitionKeyParam = new NpgsqlParameter("partitionKeyRowKey", partitionKeyRowKey);
			return (await QueryGenericRows<TValue>(query, partitionKeyParam)).FirstOrDefault();
		}

		internal async Task<bool> ReleaseLock(string partitionKey, string rowKey)
		{
			var partitionKeyRowKey = PartitionKeyRowKey(partitionKey, rowKey);
			using (var command = Connection.CreateCommand())
			{
				var partitionKeyParam = new NpgsqlParameter("partitionKeyRowKey", partitionKeyRowKey);
				command.Parameters.Add(partitionKeyParam);
				command.CommandText = "DELETE FROM \"GenericTables\" " +
									  "WHERE \"PartitionKeyRowKey\" = @partitionKeyRowKey";
				return await command.ExecuteNonQueryAsync() == 1;
			}
		}

		internal Task<IEnumerable<GenericRow<TValue>>> SelectForward<TValue>(string partitionKey)
		{
			return SelectForwardStartsWith<TValue>(partitionKey, string.Empty);
		}

		internal Task<IEnumerable<GenericRow<TValue>>> SelectForwardStartsWith<TValue>(string partitionKey, string rowKey)
		{
			var partitionKeyRowKey = PartitionKeyRowKey(partitionKey, rowKey);
			var query = $"SELECT {Columns} FROM \"GenericTables\" WHERE \"PartitionKeyRowKey\" LIKE @partitionKeyRowKey AND \"DeletedAt\" IS NULL ORDER BY \"PartitionKeyRowKey\"";
			var partitionKeyParam = new NpgsqlParameter("partitionKeyRowKey", partitionKeyRowKey + "%");
			return QueryGenericRows<TValue>(query, partitionKeyParam);
		}

		internal async Task<int> Count(string partitionKey, string rowKey)
		{
			var partitionKeyRowKey = PartitionKeyRowKey(partitionKey, rowKey);
			var query = $"SELECT COUNT(*) FROM \"GenericTables\" " +
				$"WHERE \"PartitionKeyRowKey\" LIKE @partitionKeyRowKey AND \"DeletedAt\" IS NULL";
			var partitionKeyParam = new NpgsqlParameter("partitionKeyRowKey", partitionKeyRowKey + "%");
			using (var command = Connection.CreateCommand())
			{
				command.CommandText = query;
				command.Parameters.Add(partitionKeyParam);
				return (int)(long)await command.ExecuteScalarAsync();
			}
		}

		private Task<IEnumerable<GenericRow<TValue>>> QueryGenericRows<TValue>(string query, params NpgsqlParameter[] parameters)
		{
			return QueryGenericRows<TValue>(query, !ValuesLazyLoadingIsOn, parameters);
		}
		private async Task<IEnumerable<GenericRow<TValue>>> QueryGenericRows<TValue>(string query, bool fetchValue, params NpgsqlParameter[] parameters)
		{
			List<GenericRow<TValue>> rows = new List<GenericRow<TValue>>();
			using (var command = Connection.CreateCommand())
			{
				command.CommandText = query;
				command.Parameters.AddRange(parameters);

				var partitionKeyRowKey = parameters.FirstOrDefault(p => p.ParameterName == "partitionKeyRowKey")?.Value?.ToString();
				bool likePattern = partitionKeyRowKey.EndsWith('%');
				using (var result = (NpgsqlDataReader)await command.ExecuteReaderAsync())
				{
					while (await result.ReadAsync())
					{

						partitionKeyRowKey = (likePattern ? null : partitionKeyRowKey) ?? (string)result["PartitionKeyRowKey"];
						var row = new GenericRow<TValue>()
						{
							Key = partitionKeyRowKey.Split("@@")[1]
						};
						if (fetchValue)
							row.Value = Convert<TValue>((byte[])result["Value"]);
						else
							row.FetchValue = FetchValue<TValue>(partitionKeyRowKey);
						rows.Add(row);
					}
				}
			}
			return rows;
		}

		private Func<Task<TValue>> FetchValue<TValue>(string partitionKeyRowKey)
		{
			return async () =>
			{
				var query = $"SELECT \"Value\" FROM \"GenericTables\" WHERE \"PartitionKeyRowKey\" = @partitionKeyRowKey";
				var partitionKeyRowKeyParam = new NpgsqlParameter("partitionKeyRowKey", partitionKeyRowKey);
				var row = (await QueryGenericRows<TValue>(query, true, partitionKeyRowKeyParam)).FirstOrDefault();
				if (row == null)
					return default(TValue);
				return row.Value;
			};
		}

		private TValue Convert<TValue>(byte[] value)
		{
			if (typeof(TValue) == typeof(byte[]))
				return (TValue)(object)value;
			if (typeof(TValue) == typeof(int))
				return (TValue)(object)NBitcoin.Utils.ToInt32(value, 0, true);
			return default(TValue);
		}

		string Columns => ValuesLazyLoadingIsOn ? "\"PartitionKeyRowKey\"" : "\"PartitionKeyRowKey\", \"Value\"";

		/// <summary>
		/// If true, we delete or insert immediately (ignoring DeletedAt)
		/// </summary>
		public bool ForceDelete
		{
			get;
			internal set;
		}
		public async Task<int> CommitAsync()
		{
			if (_ToCommit.Count == 0)
				return 0;
			using (var command = Connection.CreateCommand())
			{
				StringBuilder commands = new StringBuilder();
				foreach (var commit in _ToCommit)
				{
					command.Parameters.AddRange(commit.Item2);
					commands.Append(commit.Item1);
					commands.AppendLine(";");
				}
				command.CommandText = commands.ToString();
				var updated = await command.ExecuteNonQueryAsync();
				_ToCommit.Clear();
				return updated;
			}
		}
		public void Dispose()
		{
			_Parent.Return(Connection);
		}
	}
}
