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
		RepositoryTester(string name, bool caching)
		{
			_Name = name;
			ServerTester.DeleteRecursivelyWithMagicDust(name);
			_Repository = new Repository(new Serializer(Network.RegTest), name);
		}

		public void Dispose()
		{
			_Repository.Dispose();
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
