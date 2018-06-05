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
			return new NBXplorerDBContext(_ConnectionString);
		}

		public void ConfigureBuilder(DbContextOptionsBuilder builder)
		{
			builder.UseNpgsql(_ConnectionString);
		}
	}
}
