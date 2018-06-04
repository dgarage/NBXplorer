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
				if(!_Set && FetchValue != null)
				{
					_Value = FetchValue();
				}
				return _Value;
			}
			set
			{
				_Set = true;
				_Value = value;
			}
		}

		internal Func<TValue> FetchValue
		{
			get;
			set;
		}
	}

	public class GenericTable
	{
		[Key]
		public string PartitionKeyRowKey
		{
			get; set;
		}

		public byte[] Value
		{
			get; set;
		}

		public DateTimeOffset? DeletedAt
		{
			get; set;
		}
	}

	public class NBXplorerDBContext : DbContext
	{
		public NBXplorerDBContext()
		{

		}
		public NBXplorerDBContext(DbContextOptions options) : base(options)
		{
		}

		public bool ValuesLazyLoadingIsOn
		{
			get; set;
		} = true;

		public DbSet<GenericTable> GenericTables
		{
			get; set;
		}


		internal void RemoveKey(string partitionKey, string rowKey)
		{
			var partitionKeyRowKey = PartitionKeyRowKey(partitionKey, rowKey);
			var query = $"UPDATE \"GenericTables\" " +
						$"SET \"DeletedAt\" = now() " +
				        $"WHERE \"PartitionKeyRowKey\" = @partitionKeyRowKey{_ToCommit.Count}";
			var partitionKeyRowKeyParam = new NpgsqlParameter($"partitionKeyRowKey{_ToCommit.Count}", partitionKeyRowKey);
			_ToCommit.Add((query, new DbParameter[] { partitionKeyRowKeyParam }));
		}

		List<(string, DbParameter[])> _ToCommit = new List<(string, DbParameter[])>();

		internal void Insert(string partitionKey, string rowKey, byte[] data)
		{
			var partitionKeyRowKeyParam = new NpgsqlParameter($"partitionKeyRowKey{_ToCommit.Count}", PartitionKeyRowKey(partitionKey, rowKey));
			var valueParam = new NpgsqlParameter($"value{_ToCommit.Count}", data);
			_ToCommit.Add(($"INSERT INTO \"GenericTables\" ( \"PartitionKeyRowKey\", \"Value\") " +
				           $"VALUES (@partitionKeyRowKey{_ToCommit.Count}, @value{_ToCommit.Count}) " +
			               $"ON CONFLICT ( \"PartitionKeyRowKey\") DO UPDATE SET \"Value\" = @value{_ToCommit.Count} WHERE \"GenericTables\".\"DeletedAt\" IS NULL", new DbParameter[] { partitionKeyRowKeyParam, valueParam }));
		}

		private string PartitionKeyRowKey(string partitionKey, string rowKey)
		{
			Validate(partitionKey, rowKey);
			return $"{partitionKey}@@{rowKey}";
		}

		private static void Validate(string partitionKey, string rowKey)
		{
			if(partitionKey.Contains("@@", StringComparison.OrdinalIgnoreCase) || rowKey.Contains("@@", StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("PartitionKey or RowKey should not contains '@@'");
		}

		internal void Insert<T>(string partitionKey, string rowKey, T value)
		{
			if(value is byte[] b)
				Insert(partitionKey, rowKey, b);
			else if(value is int i)
				Insert(partitionKey, rowKey, NBitcoin.Utils.ToBytes((uint)i, true));
		}

		internal GenericRow<TValue> Select<TValue>(string partitionKey, string rowKey)
		{
			var partitionKeyRowKey = PartitionKeyRowKey(partitionKey, rowKey);
			var query = $"SELECT {Columns} FROM \"GenericTables\" " +
				        $"WHERE \"PartitionKeyRowKey\" = @partitionKeyRowKey AND \"DeletedAt\" IS NULL " +
						$"LIMIT 1";
			var partitionKeyParam = new NpgsqlParameter("partitionKeyRowKey", partitionKeyRowKey);
			return QueryGenericRows<TValue>(query, partitionKeyParam).FirstOrDefault();
		}

		internal IEnumerable<GenericRow<TValue>> SelectForward<TValue>(string partitionKey)
		{
			return SelectForwardStartsWith<TValue>(partitionKey, string.Empty);
		}

		internal IEnumerable<GenericRow<TValue>> SelectForwardStartsWith<TValue>(string partitionKey, string rowKey)
		{
			var partitionKeyRowKey = PartitionKeyRowKey(partitionKey, rowKey);
			var query = $"SELECT {Columns} FROM \"GenericTables\" WHERE \"PartitionKeyRowKey\" LIKE @partitionKeyRowKey AND \"DeletedAt\" IS NULL";
			var partitionKeyParam = new NpgsqlParameter("partitionKeyRowKey", partitionKeyRowKey + "%");
			return QueryGenericRows<TValue>(query, partitionKeyParam);
		}

		internal int Count(string partitionKey, string rowKey)
		{
			var partitionKeyRowKey = PartitionKeyRowKey(partitionKey, rowKey);
			var query = $"SELECT COUNT(*) FROM \"GenericTables\" " +
				$"WHERE \"PartitionKeyRowKey\" LIKE @partitionKeyRowKey AND \"DeletedAt\" IS NULL";
			var partitionKeyParam = new NpgsqlParameter("partitionKeyRowKey", partitionKeyRowKey + "%");
			using(var command = Database.GetDbConnection().CreateCommand())
			{
				Database.OpenConnection();
				command.CommandText = query;
				command.Parameters.Add(partitionKeyParam);
				return (int)(long)command.ExecuteScalar();
			}
		}

		private IEnumerable<GenericRow<TValue>> QueryGenericRows<TValue>(string query, params NpgsqlParameter[] parameters)
		{
			return QueryGenericRows<TValue>(query, !ValuesLazyLoadingIsOn, parameters);
		}
		private IEnumerable<GenericRow<TValue>> QueryGenericRows<TValue>(string query, bool fetchValue, params NpgsqlParameter[] parameters)
		{
			List<GenericRow<TValue>> rows = new List<GenericRow<TValue>>();
			using(var command = Database.GetDbConnection().CreateCommand())
			{
				command.CommandText = query;
				command.Parameters.AddRange(parameters);

				var partitionKeyRowKey = parameters.FirstOrDefault(p => p.ParameterName == "partitionKeyRowKey")?.Value?.ToString();
				bool likePattern = partitionKeyRowKey.EndsWith('%');
				Database.OpenConnection();
				using(var result = (NpgsqlDataReader)command.ExecuteReader())
				{
					while(result.Read())
					{

						partitionKeyRowKey = (likePattern ? null : partitionKeyRowKey) ?? (string)result["PartitionKeyRowKey"];
						var row = new GenericRow<TValue>()
						{
							Key = partitionKeyRowKey.Split("@@")[1]
						};
						if(fetchValue)
							row.Value = Convert<TValue>((byte[])result["Value"]);
						else
							row.FetchValue = FetchValue<TValue>(partitionKeyRowKey);
						rows.Add(row);
					}
				}
			}
			return rows;
		}

		private GenericRow<TValue> AsGenericRow<TValue>(GenericTable entity)
		{
			var splitted = entity.PartitionKeyRowKey.Split("@@");
			return new GenericRow<TValue>()
			{
				Key = splitted[1],
				Value = ValuesLazyLoadingIsOn ? default(TValue) : Convert<TValue>(entity.Value),
				FetchValue = FetchValue<TValue>(entity.PartitionKeyRowKey)
			};
		}

		private Func<TValue> FetchValue<TValue>(string partitionKeyRowKey)
		{
			return () =>
			{
				var query = $"SELECT \"Value\" FROM \"GenericTables\" WHERE \"PartitionKeyRowKey\" = @partitionKeyRowKey";
				var partitionKeyRowKeyParam = new NpgsqlParameter("partitionKeyRowKey", partitionKeyRowKey);
				var row = QueryGenericRows<TValue>(query, true, partitionKeyRowKeyParam).FirstOrDefault();
				if(row == null)
					return default(TValue);
				return row.Value;
			};
		}

		private TValue Convert<TValue>(byte[] value)
		{
			if(typeof(TValue) == typeof(byte[]))
				return (TValue)(object)value;
			if(typeof(TValue) == typeof(int))
				return (TValue)(object)NBitcoin.Utils.ToInt32(value, 0, true);
			return default(TValue);
		}

		string Columns => ValuesLazyLoadingIsOn ? "\"PartitionKeyRowKey\"" : "\"PartitionKeyRowKey\", \"Value\"";

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			var isConfigured = optionsBuilder.Options.Extensions.OfType<RelationalOptionsExtension>().Any();
			if(!isConfigured)
				optionsBuilder.UseNpgsql("User ID=postgres;Host=127.0.0.1;Port=39382;Database=nbxplorer");
		}

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<GenericTable>();
		}

		public void Commit()
		{
			CommitAsync().GetAwaiter().GetResult();
		}
		public async Task CommitAsync()
		{
			if(_ToCommit.Count == 0)
				return;
			using(var command = Database.GetDbConnection().CreateCommand())
			{
				Database.OpenConnection();
				StringBuilder commands = new StringBuilder();
				foreach(var commit in _ToCommit)
				{
					command.Parameters.AddRange(commit.Item2);
					commands.Append(commit.Item1);
					commands.AppendLine(";");
				}
				command.CommandText = commands.ToString();
				await command.ExecuteNonQueryAsync();
				_ToCommit.Clear();
			}
		}
	}
}
