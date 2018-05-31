using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer.DB
{
	public class NBXplorerContextFactory
	{
		string _ConnectionString;
		public NBXplorerContextFactory(string connectionString)
		{
			_ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}

		public NBXplorerDBContext CreateContext()
		{
			var builder = new DbContextOptionsBuilder<NBXplorerDBContext>();
			ConfigureBuilder(builder);
			return new NBXplorerDBContext(builder.Options);
		}

		public void ConfigureBuilder(DbContextOptionsBuilder builder)
		{
			builder.UseNpgsql(_ConnectionString);
		}
	}
}
