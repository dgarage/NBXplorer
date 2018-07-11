using NBitcoin;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace NBXplorer.Tests
{
	public class RepositoryTester : IDisposable
	{
		public static RepositoryTester Create(bool caching, [CallerMemberName]string name = null)
		{
			return new RepositoryTester(name, caching);
		}

		string _Name;
		private RepositoryProvider _Provider;

		RepositoryTester(string name, bool caching)
		{
			_Name = name;
			ServerTester.DeleteRecursivelyWithMagicDust(name);

			var dbFactory = new DB.NBXplorerContextFactory(Environment.GetEnvironmentVariable("TESTS_POSTGRES") ?? "User ID=postgres;Host=127.0.0.1;Port=39382;Database=nbxplorerr");
			dbFactory.Migrate();
			_Provider = new RepositoryProvider(dbFactory, new NBXplorerNetworkProvider(NetworkType.Regtest), 
											   new Configuration.ExplorerConfiguration()
											   {
												   DataDir = name,
												   ChainConfigurations = new List<Configuration.ChainConfiguration>()
												   {
													   new Configuration.ChainConfiguration()
													   {
														   CryptoCode = "BTC",
														   Rescan = false
													   }
												   }
											   });
			_Repository = _Provider.GetRepository(new NBXplorerNetworkProvider(NetworkType.Regtest).GetFromCryptoCode("BTC"));
		}

		public void Dispose()
		{
			_Provider.Dispose();
			ServerTester.DeleteRecursivelyWithMagicDust(_Name);
		}

		private Repository _Repository;
		public Repository Repository
		{
			get
			{
				return _Repository;
			}
		}
	}
}
