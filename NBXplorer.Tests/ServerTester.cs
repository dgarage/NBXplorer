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
using NBXplorer.Logging;

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

		NodeDownloadData nodeDownloadData;
		NodeDownloadData litecoinDownloadData = new NodeDownloadData()
		{
			Version = "0.14.2",
			Windows = new NodeOSDownloadData()
			{
				DownloadLink = "https://download.litecoin.org/litecoin-{0}/win/litecoin-{0}-win64.zip",
				Archive = "litecoin-{0}-win32.zip",
				Executable = "litecoin-{0}/bin/litecoind.exe"
			},
			Linux = new NodeOSDownloadData()
			{
				DownloadLink = "https://download.litecoin.org/litecoin-{0}/linux/litecoin-{0}-x86_64-linux-gnu.tar.gz",
				Archive = "litecoin-{0}-x86_64-linux-gnu.tar.gz",
				Executable = "litecoin-{0}/bin/litecoind"
			},
			Mac = new NodeOSDownloadData()
			{
				DownloadLink = "https://download.litecoin.org/litecoin-{0}/osx/litecoin-{0}-osx64.tar.gz",
				Archive = "litecoin-{0}-osx64.tar.gz",
				Executable = "litecoin-{0}/bin/litecoind"
			}
		};

		NodeDownloadData bitcoinDownloadData = new NodeDownloadData()
		{
			Version = "0.16.0",
			Linux = new NodeOSDownloadData()
			{
				Archive = "bitcoin-{0}-x86_64-linux-gnu.tar.gz",
				DownloadLink = "https://bitcoin.org/bin/bitcoin-core-{0}/bitcoin-{0}-x86_64-linux-gnu.tar.gz",
				Executable = "bitcoin-{0}/bin/bitcoind"
			},
			Mac = new NodeOSDownloadData()
			{
				Archive = "bitcoin-{0}-osx64.tar.gz",
				DownloadLink = "https://bitcoin.org/bin/bitcoin-core-{0}/bitcoin-{0}-osx64.tar.gz",
				Executable = "bitcoin-{0}/bin/bitcoind"
			},
			Windows = new NodeOSDownloadData()
			{
				Executable = "bitcoin-{0}/bin/bitcoind.exe",
				DownloadLink = "https://bitcoin.org/bin/bitcoin-core-{0}/bitcoin-{0}-win32.zip",
				Archive = "bitcoin-{0}-win32.zip"
			}
		};

		public ServerTester(string directory)
		{
			nodeDownloadData = bitcoinDownloadData;
			var networkString = "regtest";
			try
			{
				var rootTestData = "TestData";
				directory = Path.Combine(rootTestData, directory);
				_Directory = directory;
				if(!Directory.Exists(rootTestData))
					Directory.CreateDirectory(rootTestData);

				NodeBuilder = CreateNodeBuilder(directory);



				User1 = NodeBuilder.CreateNode();
				User2 = NodeBuilder.CreateNode();
				Explorer = NodeBuilder.CreateNode();
				foreach(var node in NodeBuilder.Nodes)
					node.WhiteBind = true;
				NodeBuilder.StartAll();

				User1.CreateRPCClient().Generate(1);
				User1.Sync(Explorer, true);
				Explorer.CreateRPCClient().Generate(1);
				Explorer.Sync(User2, true);
				User2.CreateRPCClient().Generate(101);
				User1.Sync(User2, true);

				var creds = RPCCredentialString.Parse(Explorer.AuthenticationString).UserPassword;
				var datadir = Path.Combine(directory, "explorer");
				DeleteRecursivelyWithMagicDust(datadir);
				List<(string key, string value)> keyValues = new List<(string key, string value)>();
				keyValues.Add(("conf", Path.Combine(directory, "explorer", "settings.config")));
				keyValues.Add(("datadir", datadir));
				keyValues.Add(("network", networkString));
				keyValues.Add(("verbose", "1"));
				keyValues.Add(("btcrpcuser", creds.UserName));
				keyValues.Add(("btcrpcpassword", creds.Password));
				keyValues.Add(("btcrpcurl", Explorer.CreateRPCClient().Address.AbsoluteUri));
				keyValues.Add(("cachechain", "0"));
				keyValues.Add(("rpcnotest", "1"));
				keyValues.Add(("mingapsize", "2"));
				keyValues.Add(("maxgapsize", "4"));
				keyValues.Add(("btcstartheight", Explorer.CreateRPCClient().GetBlockCount().ToString()));
				keyValues.Add(("btcnodeendpoint", $"{Explorer.Endpoint.Address}:{Explorer.Endpoint.Port}"));

				var args = keyValues.SelectMany(kv => new[] { $"--{kv.key}", kv.value }).ToArray();
				Host = new WebHostBuilder()
					.UseConfiguration(new DefaultConfiguration().CreateConfiguration(args))
					.UseKestrel()
					.ConfigureLogging(l =>
					{
						l.SetMinimumLevel(LogLevel.Information)
							.AddFilter("Microsoft", LogLevel.Error)
							.AddFilter("Hangfire", LogLevel.Error)
							.AddFilter("NBXplorer.Authentication.BasicAuthenticationHandler", LogLevel.Critical)
							.AddProvider(Logs.LogProvider);
					})
					.UseStartup<Startup>()
					.Build();

				string cryptoCode = "BTC";
				RPC = ((RPCClientProvider)Host.Services.GetService(typeof(RPCClientProvider))).GetRPCClient(cryptoCode);
				var nbxnetwork = ((NBXplorerNetworkProvider)Host.Services.GetService(typeof(NBXplorerNetworkProvider))).GetFromCryptoCode(cryptoCode);
				Network = nbxnetwork.NBitcoinNetwork;
				var conf = (ExplorerConfiguration)Host.Services.GetService(typeof(ExplorerConfiguration));
				Host.Start();

				_Client = new ExplorerClient(nbxnetwork, Address);
				_Client.SetCookieAuth(Path.Combine(conf.DataDir, ".cookie"));
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		private NodeBuilder CreateNodeBuilder(string directory)
		{
			return NodeBuilder.Create(nodeDownloadData, directory);
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
