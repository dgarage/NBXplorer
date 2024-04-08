using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBXplorer.Backends;
using NBXplorer.Backends.Postgres;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace NBXplorer.Tests
{
	public class RepositoryTester : IDisposable
	{
		public static RepositoryTester Create(bool caching, [CallerMemberName]string name = null)
		{
			return new RepositoryTester(name, caching);
		}

		string _Name;
		private IRepositoryProvider _Provider;

		RepositoryTester(string name, bool caching)
		{
			_Name = name;
			var conf = new Configuration.ExplorerConfiguration()
			{
				DataDir = name,
				ChainConfigurations = new List<Configuration.ChainConfiguration>()
													   {
														   new Configuration.ChainConfiguration()
														   {
															   CryptoCode = "BTC",
															   Rescan = false
														   }
													   },
				NetworkProvider = new NBXplorerNetworkProvider(ChainName.Regtest)
			};
			ServiceCollection services = new ServiceCollection();
			services.AddSingleton(conf);
			services.AddSingleton(KeyPathTemplates.Default);
			services.AddSingleton(new NBXplorerNetworkProvider(ChainName.Regtest));

			services.AddLogging();
			services.AddSingleton<DbConnectionFactory>();
			ConfigurationBuilder builder = new ConfigurationBuilder();
			builder.AddInMemoryCollection(new[] { new KeyValuePair<string, string>("POSTGRES", ServerTester.GetTestPostgres(null, name)) });
			services.AddSingleton<IConfiguration>(builder.Build());
			services.AddSingleton<IRepositoryProvider, PostgresRepositoryProvider>();
			services.AddSingleton<HostedServices.DatabaseSetupHostedService>();

			var provider = services.BuildServiceProvider();
			_Provider = provider.GetService<IRepositoryProvider>();
			provider.GetRequiredService<HostedServices.DatabaseSetupHostedService>().StartAsync(default).GetAwaiter().GetResult();
			_Provider.StartAsync(default).GetAwaiter().GetResult();
			_Repository = _Provider.GetRepository(new NBXplorerNetworkProvider(ChainName.Regtest).GetFromCryptoCode("BTC"));
		}

		public void Dispose()
		{
			_Provider.StopAsync(default).GetAwaiter().GetResult();
			ServerTester.DeleteFolderRecursive(_Name);
		}

		private IRepository _Repository;
		public IRepository Repository
		{
			get
			{
				return _Repository;
			}
		}
	}
}
