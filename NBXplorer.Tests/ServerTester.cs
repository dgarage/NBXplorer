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
using NBXplorer.DerivationStrategy;
using System.Net.Http;

namespace NBXplorer.Tests
{
	public partial class ServerTester : IDisposable
	{
		private readonly string _Directory;

		public static ServerTester Create([CallerMemberNameAttribute]string caller = null)
		{
			return new ServerTester(caller);
		}
		public static ServerTester CreateNoAutoStart([CallerMemberNameAttribute]string caller = null)
		{
			return new ServerTester(caller, false);
		}

		public void Dispose()
		{
			if (Host != null)
			{
				Host.Dispose();
				Host = null;
			}
			if (NodeBuilder != null)
			{
				NodeBuilder.Dispose();
				NodeBuilder = null;
			}
		}

		NodeDownloadData nodeDownloadData;

		public string CryptoCode
		{
			get; set;
		}

		public string Caller { get; }
		public ServerTester(string directory, bool autoStart = true)
		{
			SetEnvironment();
			Caller = directory;
			var rootTestData = "TestData";
			directory = Path.Combine(rootTestData, directory);
			_Directory = directory;
			if (!Directory.Exists(rootTestData))
				Directory.CreateDirectory(rootTestData);
			if (autoStart)
				Start();
		}

		public async Task Load(string dataName)
		{
			datadir = Path.Combine(_Directory, "explorer");
			if (Directory.Exists(datadir))
				DeleteFolderRecursive(datadir);
			Directory.CreateDirectory(_Directory);
			Directory.CreateDirectory(datadir);
			datadir = Path.Combine(datadir, "RegTest", "db");
			Directory.CreateDirectory(datadir);
			foreach (var file in Directory.GetFiles(Path.Combine("Data", dataName)))
			{
				File.Copy(file, Path.Combine(datadir, Path.GetFileName(file)));
			}
			LoadedData = true;
			await using var db = await DBTrie.DBTrieEngine.OpenFromFolder(datadir);
			using var tx = await db.OpenTransaction();
			await tx.GetTable("IndexProgress").Delete();
		}

		public void Start()
		{
			try
			{
				var cryptoSettings = new NBXplorerNetworkProvider(NetworkType.Regtest).GetFromCryptoCode(CryptoCode);
				NodeBuilder = NodeBuilder.Create(nodeDownloadData, Network, _Directory);
				if (KeepPreviousData)
					NodeBuilder.CleanBeforeStartingNode = false;
				Explorer = NodeBuilder.CreateNode();
				Explorer.ConfigParameters.Add("txindex", "1");
				foreach (var node in NodeBuilder.Nodes)
				{
					node.WhiteBind = true;
					node.CookieAuth = cryptoSettings.SupportCookieAuthentication;
				}
				NodeBuilder.StartAll();
				if (!KeepPreviousData)
					Explorer.CreateRPCClient().EnsureGenerate(Network.Consensus.CoinbaseMaturity + 1);

				datadir = Path.Combine(_Directory, "explorer");
				if (!KeepPreviousData && !LoadedData)
					DeleteFolderRecursive(datadir);
				StartNBXplorer();
				this.Client.WaitServerStarted();
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		public int TrimEvents { get; set; } = -1;
		public bool UseRabbitMQ { get; set; } = false;
		private void StartNBXplorer()
		{
			var port = CustomServer.FreeTcpPort();
			List<(string key, string value)> keyValues = new List<(string key, string value)>();
			keyValues.Add(("conf", Path.Combine(datadir, "settings.config")));
			keyValues.Add(("datadir", datadir));
			keyValues.Add(("port", port.ToString()));
			keyValues.Add(("network", "regtest"));
			keyValues.Add(("instancename", Caller));
			keyValues.Add(("chains", CryptoCode.ToLowerInvariant()));
			keyValues.Add(("verbose", "1"));
			keyValues.Add(($"{CryptoCode.ToLowerInvariant()}rpcauth", Explorer.GetRPCAuth()));
			keyValues.Add(($"{CryptoCode.ToLowerInvariant()}rpcurl", Explorer.CreateRPCClient().Address.AbsoluteUri));
			keyValues.Add(("cachechain", "0"));
			keyValues.Add(("exposerpc", "1"));
			keyValues.Add(("rpcnotest", "1"));
			keyValues.Add(("trimevents", TrimEvents.ToString()));
			keyValues.Add(("mingapsize", "3"));
			keyValues.Add(("maxgapsize", "8"));
			keyValues.Add(($"{CryptoCode.ToLowerInvariant()}startheight", Explorer.CreateRPCClient().GetBlockCount().ToString()));
			keyValues.Add(($"{CryptoCode.ToLowerInvariant()}nodeendpoint", $"{Explorer.Endpoint.Address}:{Explorer.Endpoint.Port}"));
			keyValues.Add(("asbcnstr", AzureServiceBusTestConfig.ConnectionString));
			keyValues.Add(("asbblockq", AzureServiceBusTestConfig.NewBlockQueue));
			keyValues.Add(("asbtranq", AzureServiceBusTestConfig.NewTransactionQueue));
			keyValues.Add(("asbblockt", AzureServiceBusTestConfig.NewBlockTopic));
			keyValues.Add(("asbtrant", AzureServiceBusTestConfig.NewTransactionTopic));
			if (UseRabbitMQ)
			{
				keyValues.Add(("rmqhost", RabbitMqTestConfig.RabbitMqHostName));
				keyValues.Add(("rmqvirtual", RabbitMqTestConfig.RabbitMqVirtualHost));
				keyValues.Add(("rmquser", RabbitMqTestConfig.RabbitMqUsername));
				keyValues.Add(("rmqpass", RabbitMqTestConfig.RabbitMqPassword));
				keyValues.Add(("rmqtranex", RabbitMqTestConfig.RabbitMqTransactionExchange));
				keyValues.Add(("rmqblockex", RabbitMqTestConfig.RabbitMqBlockExchange));
			}
			var args = keyValues.SelectMany(kv => new[] { $"--{kv.key}", kv.value }
			.Concat(new[] { $"--{CryptoCode.ToLowerInvariant()}hastxindex" })).ToArray();
			Host = new WebHostBuilder()
				.UseConfiguration(new DefaultConfiguration().CreateConfiguration(args))
				.UseKestrel()
				.ConfigureLogging(l =>
				{
					l.SetMinimumLevel(LogLevel.Information)
						.AddFilter("System.Net.Http.HttpClient", LogLevel.Error)
						.AddFilter("Microsoft", LogLevel.Error)
						.AddFilter("Hangfire", LogLevel.Error)
						.AddFilter("NBXplorer.Authentication.BasicAuthenticationHandler", LogLevel.Critical)
						.AddProvider(Logs.LogProvider);
				})
				.UseStartup<Startup>()
				.Build();

			RPC = ((RPCClientProvider)Host.Services.GetService(typeof(RPCClientProvider))).GetRPCClient(CryptoCode);
			NBXplorerNetwork = ((NBXplorerNetworkProvider)Host.Services.GetService(typeof(NBXplorerNetworkProvider))).GetFromCryptoCode(CryptoCode);
			var conf = (ExplorerConfiguration)Host.Services.GetService(typeof(ExplorerConfiguration));
			Host.Start();
			Configuration = conf;
			_Client = NBXplorerNetwork.CreateExplorerClient(Address);
			HttpClient = ((IHttpClientFactory)Host.Services.GetService(typeof(IHttpClientFactory))).CreateClient();
			HttpClient.BaseAddress = Address;
			_Client.SetCookieAuth(Path.Combine(conf.DataDir, ".cookie"));
			Notifications = _Client.CreateLongPollingNotificationSession();
		}

		public HttpClient HttpClient { get; internal set; }

		string datadir;

		public void ResetExplorer(bool deleteAll = true)
		{
			Host.Dispose();
			if (deleteAll)
				DeleteFolderRecursive(datadir);
			StartNBXplorer();
			this.Client.WaitServerStarted();
		}

		public LongPollingNotificationSession Notifications { get; set; }
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

		public T GetService<T>()
		{
			return ((T)(Host.Services.GetService(typeof(T))));
		}

		public ExplorerConfiguration Configuration { get; private set; }

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
			get
			{
				return NBXplorerNetwork.NBitcoinNetwork;
			}
		}
		public NBXplorerNetwork NBXplorerNetwork
		{
			get;
			internal set;
		}

		private static bool TryDelete(string directory, bool throws)
		{
			try
			{
				DeleteFolderRecursive(directory);
				return true;
			}
			catch (DirectoryNotFoundException)
			{
				return true;
			}
			catch (Exception)
			{
				if (throws)
					throw;
			}
			return false;
		}

		public static void DeleteFolderRecursive(string destinationDir)
		{
			for (var i = 1; i <= 10; i++)
			{
				try
				{
					Directory.Delete(destinationDir, true);
					return;
				}
				catch (DirectoryNotFoundException)
				{
					return;
				}
				catch (IOException)
				{
					Thread.Sleep(100 * i);
					continue;
				}
				catch (UnauthorizedAccessException)
				{
					Thread.Sleep(100 * i);
					continue;
				}
			}
			throw new IOException($"Impossible to delete folder {destinationDir}");
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
			foreach (FileInfo fi in source.GetFiles())
			{
				fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
			}

			// Copy each subdirectory using recursion.
			foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
			{
				DirectoryInfo nextTargetSubDir =
					target.CreateSubdirectory(diSourceSubDir.Name);
				CopyAll(diSourceSubDir, nextTargetSubDir);
			}
		}

		public BitcoinSecret PrivateKeyOf(BitcoinExtKey key, string path)
		{
			return new BitcoinSecret(key.ExtKey.Derive(new KeyPath(path)).PrivateKey, Network);
		}

		public BitcoinAddress AddressOf(BitcoinExtKey key, string path)
		{
			if (this.RPC.Capabilities.SupportSegwit)
				return key.ExtKey.Derive(new KeyPath(path)).Neuter().PubKey.WitHash.GetAddress(Network);
			else
				return key.ExtKey.Derive(new KeyPath(path)).Neuter().PubKey.Hash.GetAddress(Network);
		}

		public BitcoinAddress AddressOf(DerivationStrategyBase scheme, string path)
		{
			return scheme.GetDerivation(KeyPath.Parse(path)).ScriptPubKey.GetDestinationAddress(Network);
		}

		public DirectDerivationStrategy CreateDerivationStrategy(ExtPubKey pubKey = null)
		{
			return (DirectDerivationStrategy)CreateDerivationStrategy(pubKey, false);
		}
		public DerivationStrategyBase CreateDerivationStrategy(ExtPubKey pubKey, bool p2sh)
		{
			key = key ?? new ExtKey();
			pubKey = pubKey ?? key.Neuter();
			string suffix = this.RPC.Capabilities.SupportSegwit ? "" : "-[legacy]";
			suffix += p2sh ? "-[p2sh]" : "";
			scriptPubKeyType = p2sh ? ScriptPubKeyType.SegwitP2SH : ScriptPubKeyType.Segwit;
			return NBXplorerNetwork.DerivationStrategyFactory.Parse($"{pubKey.ToString(this.Network)}{suffix}");
		}
		ExtKey key;
		ScriptPubKeyType scriptPubKeyType;
		public void SignPSBT(PSBT psbt)
		{
			psbt.SignAll(key.AsHDScriptPubKey(scriptPubKeyType), key);
		}

		public bool RPCStringAmount
		{
			get; set;
		} = true;
		public bool KeepPreviousData { get; set; }
		public bool LoadedData { get; private set; }

		public uint256 SendToAddress(BitcoinAddress address, Money amount)
		{
			return SendToAddressAsync(address, amount).GetAwaiter().GetResult();
		}

		public uint256 SendToAddress(Script scriptPubKey, Money amount)
		{
			return SendToAddressAsync(scriptPubKey.GetDestinationAddress(Network), amount).GetAwaiter().GetResult();
		}
		public Task<uint256> SendToAddressAsync(Script scriptPubKey, Money amount)
		{
			return SendToAddressAsync(scriptPubKey.GetDestinationAddress(Network), amount);
		}

		public async Task<uint256> SendToAddressAsync(BitcoinAddress address, Money amount)
		{
			List<object> parameters = new List<object>();
			parameters.Add(address.ToString());
			if (RPCStringAmount)
				parameters.Add(amount.ToString());
			else
				parameters.Add(amount.ToDecimal(MoneyUnit.BTC));
			var resp = await RPC.SendCommandAsync(RPCOperations.sendtoaddress, parameters.ToArray());
			return uint256.Parse(resp.Result.ToString());
		}

		internal void WaitSynchronized()
		{
			using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
			{
				while (true)
				{
					cts.Token.ThrowIfCancellationRequested();
					var status = Client.GetStatus();
					if (status.SyncHeight == status.BitcoinStatus.Blocks)
					{
						break;
					}
					Thread.Sleep(50);
				}
			}
		}
	}
}
