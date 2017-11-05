using System.Linq;
using Microsoft.Extensions.Logging;
using NBXplorer.Configuration;
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
using NBitcoin.RPC;
using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;

namespace NBXplorer.Tests
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
				var cachedNodes = Path.Combine(rootTestData, "CachedNodes");
				directory = Path.Combine(rootTestData, directory);
				_Directory = directory;
				if(!Directory.Exists(rootTestData))
					Directory.CreateDirectory(rootTestData);
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

				NodeBuilder = NodeBuilder.Create(directory, "0.15.0");
				NodeBuilder.CleanBeforeStartingNode = false;
				Copy(cachedNodes, directory);


				User1 = NodeBuilder.CreateNode();
				User2 = NodeBuilder.CreateNode();
				Explorer = NodeBuilder.CreateNode();
				foreach(var node in NodeBuilder.Nodes)
					node.WhiteBind = true;
				NodeBuilder.StartAll();

				var creds = ExtractCredentials(File.ReadAllText(Explorer.Config));

				List<(string key, string value)> keyValues = new List<(string key, string value)>();
				keyValues.Add(("conf", Path.Combine(directory, "explorer", "settings.config")));
				keyValues.Add(("datadir", Path.Combine(directory, "explorer")));
				keyValues.Add(("network", "regtest"));
				keyValues.Add(("verbose", "1"));
				keyValues.Add(("rpcuser", creds.UserName));
				keyValues.Add(("rpcpassword", creds.Password));
				keyValues.Add(("rpcurl", Explorer.CreateRPCClient().Address.AbsoluteUri));
				keyValues.Add(("rpcnotest", "1"));
				keyValues.Add(("startheight", Explorer.CreateRPCClient().GetBlockCount().ToString()));
				keyValues.Add(("nodeendpoint", $"{Explorer.Endpoint.Address}:{Explorer.Endpoint.Port}"));

				var args = keyValues.SelectMany(kv => new[] { $"--{kv.key}", kv.value }).ToArray();
				Host = new WebHostBuilder()
					.UseConfiguration(new DefaultConfiguration().CreateConfiguration(args))
					.UseKestrel()
					.UseStartup<Startup>()
					.Build();
				RPC = (RPCClient)Host.Services.GetService(typeof(RPCClient));
				Network = (Network)Host.Services.GetService(typeof(Network));
				var conf = (ExplorerConfiguration)Host.Services.GetService(typeof(ExplorerConfiguration));
				Host.Start();

				_Client = new ExplorerClient(Network, Address);
				_Client.SetCookieAuth(Path.Combine(conf.DataDir, ".cookie"));
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

		private NetworkCredential ExtractCredentials(string config)
		{
			var user = Regex.Match(config, "rpcuser=([^\r\n]*)");
			var pass = Regex.Match(config, "rpcpassword=([^\r\n]*)");
			return new NetworkCredential(user.Groups[1].Value, pass.Groups[1].Value);
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
				return _Client;
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

		public RPCClient RPC
		{
			get; set;
		}

		public Network Network
		{
			get;
			internal set;
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
					Thread.Sleep(100 * gnomes);
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
