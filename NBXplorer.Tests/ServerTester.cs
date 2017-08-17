using System.Linq;
using ElementsExplorer.Configuration;
using Microsoft.AspNetCore.Hosting;
using NBitcoin;
using NBitcoin.Tests;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace ElementsExplorer.Tests
{
	public class ServerTester : IDisposable
	{
		private readonly string _Directory;

		public static ServerTester Create([CallerMemberNameAttribute]string caller = null)
		{
			return new ServerTester(caller);
		}

		public void Dispose()
		{
			if(Host != null)
			{
				Host.Dispose();
				Host = null;
			}
			if(Runtime != null)
			{
				Runtime.Dispose();
				Runtime = null;
			}
			if(NodeBuilder != null)
			{
				NodeBuilder.Dispose();
				NodeBuilder = null;
			}
		}

		public ServerTester(string directory)
		{
			try
			{

				var rootTestData = "TestData";
				var cachedNodes = "TestData/CachedNodes";
				directory = rootTestData + "/" + directory;
				_Directory = directory;
				if(!Directory.Exists(rootTestData))
					Directory.CreateDirectory(rootTestData);
				Directory.Delete(cachedNodes, true);
				if(!Directory.Exists(cachedNodes))
				{
					Directory.CreateDirectory(cachedNodes);
					RunScenario(cachedNodes);
				}

				if(!TryDelete(directory, false))
				{
					foreach(var process in Process.GetProcessesByName("bitcoind"))
					{
						if(process.MainModule.FileName.Replace("\\", "/").StartsWith(Path.GetFullPath(rootTestData).Replace("\\", "/"), StringComparison.Ordinal))
						{
							process.Kill();
							process.WaitForExit();
						}
					}
					TryDelete(directory, true);
				}

				NodeBuilder = NodeBuilder.Create(directory);
				NodeBuilder.CleanBeforeStartingNode = false;
				Copy(cachedNodes, directory);


				User1 = NodeBuilder.CreateNode();
				User2 = NodeBuilder.CreateNode();
				Explorer = NodeBuilder.CreateNode();
				NodeBuilder.StartAll();

				var a = Explorer.CreateRPCClient().GetBlockCount();
				var creds = ExtractCredentials(File.ReadAllText(Explorer.Config));
				var conf = new ExplorerConfiguration();
				conf.DataDir = Path.Combine(directory, "explorer");
				conf.Network = Network.RegTest;
				conf.RPC = new RPCArgs()
				{
					User = creds.Item1,
					Password = creds.Item2,
					Url = Explorer.CreateRPCClient().Address,
					NoTest = true
				};
				conf.NodeEndpoint = Explorer.Endpoint;

				Runtime = conf.CreateRuntime();

				Runtime.Repository.SetIndexProgress(new BlockLocator() { Blocks = { Runtime.RPC.GetBestBlockHash() } });

				Runtime.StartNodeListener(conf.StartHeight);
				Host = Runtime.CreateWebHost();
				Host.Start();
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		private void RunScenario(string directory)
		{
			NodeBuilder = NodeBuilder.Create(directory);
			User1 = NodeBuilder.CreateNode();
			User2 = NodeBuilder.CreateNode();
			Explorer = NodeBuilder.CreateNode();
			NodeBuilder.StartAll();
			User1.CreateRPCClient().Generate(1);
			User1.Sync(Explorer, true);
			Explorer.CreateRPCClient().Generate(1);
			Explorer.Sync(User2, true);
			User2.CreateRPCClient().Generate(101);
			User1.Sync(User2, true);
			var a = User1.CreateRPCClient().GetBlockCount();
			var b = User1.CreateRPCClient().GetBlockCount();
			var c = User1.CreateRPCClient().GetBlockCount();
			Task.WaitAll(new Task[]
			{
				User1.CreateRPCClient().SendCommandAsync("stop"),
				User2.CreateRPCClient().SendCommandAsync("stop"),
				Explorer.CreateRPCClient().SendCommandAsync("stop")
			}.ToArray());

			User1.WaitForExit();
			User2.WaitForExit();
			Explorer.WaitForExit();
			NodeBuilder = null;
		}

		private Tuple<string, string> ExtractCredentials(string config)
		{
			var user = Regex.Match(config, "rpcuser=([^\r\n]*)");
			var pass = Regex.Match(config, "rpcpassword=([^\r\n]*)");
			return Tuple.Create(user.Groups[1].Value, pass.Groups[1].Value);
		}

		public Uri Address
		{
			get
			{

				var address = ((KestrelServer)(Host.Services.GetService(typeof(IServer)))).Features.Get<IServerAddressesFeature>().Addresses.FirstOrDefault();
				return new Uri(address);
			}
		}

		ExplorerClient _Client;
		public ExplorerClient Client
		{
			get
			{
				return _Client = _Client ?? new ExplorerClient(Runtime.Network, Address);
			}
		}

		public CoreNode Explorer
		{
			get; set;
		}

		public CoreNode User1
		{
			get; set;
		}

		public CoreNode User2
		{
			get; set;
		}

		public NodeBuilder NodeBuilder
		{
			get; set;
		}

		public ExplorerRuntime Runtime
		{
			get; set;
		}

		public IWebHost Host
		{
			get; set;
		}

		public string BaseDirectory
		{
			get
			{
				return _Directory;
			}
		}

		private static bool TryDelete(string directory, bool throws)
		{
			try
			{
				DeleteRecursivelyWithMagicDust(directory);
				return true;
			}
			catch(DirectoryNotFoundException)
			{
				return true;
			}
			catch(Exception)
			{
				if(throws)
					throw;
			}
			return false;
		}

		// http://stackoverflow.com/a/14933880/2061103
		public static void DeleteRecursivelyWithMagicDust(string destinationDir)
		{
			const int magicDust = 10;
			for(var gnomes = 1; gnomes <= magicDust; gnomes++)
			{
				try
				{
					Directory.Delete(destinationDir, true);
				}
				catch(DirectoryNotFoundException)
				{
					return;  // good!
				}
				catch(IOException)
				{
					if(gnomes == magicDust)
						throw;
					// System.IO.IOException: The directory is not empty
					System.Diagnostics.Debug.WriteLine("Gnomes prevent deletion of {0}! Applying magic dust, attempt #{1}.", destinationDir, gnomes);

					// see http://stackoverflow.com/questions/329355/cannot-delete-directory-with-directory-deletepath-true for more magic
					Thread.Sleep(100);
					continue;
				}
				catch(UnauthorizedAccessException)
				{
					if(gnomes == magicDust)
						throw;
					// Wait, maybe another software make us authorized a little later
					System.Diagnostics.Debug.WriteLine("Gnomes prevent deletion of {0}! Applying magic dust, attempt #{1}.", destinationDir, gnomes);

					// see http://stackoverflow.com/questions/329355/cannot-delete-directory-with-directory-deletepath-true for more magic
					Thread.Sleep(100);
					continue;
				}
				return;
			}
			// depending on your use case, consider throwing an exception here
		}

		static void Copy(string sourceDirectory, string targetDirectory)
		{
			DirectoryInfo diSource = new DirectoryInfo(sourceDirectory);
			DirectoryInfo diTarget = new DirectoryInfo(targetDirectory);

			CopyAll(diSource, diTarget);
		}

		static void CopyAll(DirectoryInfo source, DirectoryInfo target)
		{
			Directory.CreateDirectory(target.FullName);

			// Copy each file into the new directory.
			foreach(FileInfo fi in source.GetFiles())
			{
				Console.WriteLine(@"Copying {0}\{1}", target.FullName, fi.Name);
				fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
			}

			// Copy each subdirectory using recursion.
			foreach(DirectoryInfo diSourceSubDir in source.GetDirectories())
			{
				DirectoryInfo nextTargetSubDir =
					target.CreateSubdirectory(diSourceSubDir.Name);
				CopyAll(diSourceSubDir, nextTargetSubDir);
			}
		}

	}
}
