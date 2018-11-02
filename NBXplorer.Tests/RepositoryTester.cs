using NBitcoin;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
			ServerTester.DeleteFolderRecursive(name);
			_Provider = new RepositoryProvider(new NBXplorerNetworkProvider(NetworkType.Regtest), 
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
			ServerTester.DeleteFolderRecursive(_Name);
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
