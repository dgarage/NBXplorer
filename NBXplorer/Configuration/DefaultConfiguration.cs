using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using NBXplorer.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading.Tasks;
using NBitcoin;
using System.Text;
using CommandLine;

namespace NBXplorer.Configuration
{
	public class DefaultConfiguration : StandardConfiguration.DefaultConfiguration
	{
		protected override CommandLineApplication CreateCommandLineApplicationCore()
		{
			var provider = new NBXplorerNetworkProvider(NetworkType.Mainnet);
			var chains = string.Join(",", provider.GetAll().Select(n => n.CryptoCode.ToLowerInvariant()).ToArray());
			CommandLineApplication app = new CommandLineApplication(true)
			{
				FullName = "NBXplorer\r\nLightweight block explorer for tracking HD wallets",
				Name = "NBXplorer"
			};
			app.HelpOption("-? | -h | --help");
			app.Option("-n | --network", $"Set the network among (mainnet,testnet,regtest) (default: mainnet)", CommandOptionType.SingleValue);
			app.Option("--testnet | -testnet", $"Use testnet", CommandOptionType.BoolValue);
			app.Option("--regtest | -regtest", $"Use regtest", CommandOptionType.BoolValue);
			app.Option("--chains", $"Chains to support comma separated (default: btc, available: {chains})", CommandOptionType.SingleValue);

			foreach(var network in provider.GetAll())
			{
				var crypto = network.CryptoCode.ToLowerInvariant();
				app.Option($"--{crypto}rescan", $"Rescan from startheight", CommandOptionType.BoolValue);
				app.Option($"--{crypto}rpcuser", $"RPC authentication method 1: The RPC user (default: using cookie auth from default network folder)", CommandOptionType.SingleValue);
				app.Option($"--{crypto}rpcpassword", $"RPC authentication method 1: The RPC password (default: using cookie auth from default network folder)", CommandOptionType.SingleValue);
				app.Option($"--{crypto}rpccookiefile", $"RPC authentication method 2: The RPC cookiefile (default: using cookie auth from default network folder)", CommandOptionType.SingleValue);
				app.Option($"--{crypto}rpcauth", $"RPC authentication method 3: user:password or cookiefile=path (default: using cookie auth from default network folder)", CommandOptionType.SingleValue);
				app.Option($"--{crypto}rpcurl", $"The RPC server url (default: default rpc server depended on the network)", CommandOptionType.SingleValue);
				app.Option($"--{crypto}startheight", $"The height where starting the scan (default: where your rpc server was synched when you first started this program)", CommandOptionType.SingleValue);
				app.Option($"--{crypto}nodeendpoint", $"The p2p connection to a Bitcoin node, make sure you are whitelisted (default: default p2p node on localhost, depends on network)", CommandOptionType.SingleValue);
			}

			app.Option("--asbcnstr", "[For Azure Service Bus] Azure Service Bus Connection string. New Block and New Transaction messages will be pushed to queues when this values is set", CommandOptionType.SingleValue);
			app.Option("--asbblockq", "[For Azure Service Bus] Name of Queue to push new block message to. Leave blank to turn off", CommandOptionType.SingleValue);
			app.Option("--asbtranq", "[For Azure Service Bus] Name of Queue to push new transaction message to. Leave blank to turn off", CommandOptionType.SingleValue);
			app.Option("--asbblockt", "[For Azure Service Bus] Name of Topic to push new block message to. Leave blank to turn off", CommandOptionType.SingleValue);
			app.Option("--asbtrant", "[For Azure Service Bus] Name of Topic to push new transaction message to. Leave blank to turn off", CommandOptionType.SingleValue);
			app.Option("--maxgapsize", $"The maximum gap address count on which the explorer will track derivation schemes (default: 30)", CommandOptionType.SingleValue);
			app.Option("--mingapsize", $"The minimum gap address count on which the explorer will track derivation schemes (default: 20)", CommandOptionType.SingleValue);
			app.Option("--noauth", $"Disable cookie authentication", CommandOptionType.BoolValue);
			app.Option("--autopruning", $"EXPERIMENTAL: If getting UTXOs takes more than x seconds, NBXplorer will prune old transactions, disabled if set to -1 (default: -1)", CommandOptionType.SingleValue);
			app.Option("--cachechain", $"Whether the chain of header is locally cached for faster startup (default: true)", CommandOptionType.SingleValue);
			app.Option("--rpcnotest", $"Faster start because RPC connection testing skipped (default: false)", CommandOptionType.SingleValue);
			app.Option("-v | --verbose", $"Verbose logs (default: true)", CommandOptionType.SingleValue);
			return app;
		}


		public override string EnvironmentVariablePrefix => "NBXPLORER_";
		protected override string GetDefaultDataDir(IConfiguration conf)
		{
			return GetDefaultSettings(conf).DefaultDataDirectory;
		}

		protected override string GetDefaultConfigurationFile(IConfiguration conf)
		{
			var network = GetDefaultSettings(conf);
			var dataDir = conf["datadir"];
			if(dataDir == null)
				return network.DefaultConfigurationFile;
			var fileName = Path.GetFileName(network.DefaultConfigurationFile);
			var chainDir = Path.GetFileName(Path.GetDirectoryName(network.DefaultConfigurationFile));
			chainDir = Path.Combine(dataDir, chainDir);
			try
			{
				if(!Directory.Exists(chainDir))
					Directory.CreateDirectory(chainDir);
			}
			catch { }
			return Path.Combine(chainDir, fileName);
		}

		public static NetworkType GetNetworkType(IConfiguration conf)
		{
			var network = conf.GetOrDefault<string>("network", null);
			if(network != null)
			{
				var n = Network.GetNetwork(network);
				if(n == null)
				{
					throw new ConfigException($"Invalid network parameter '{network}'");
				}
				return n.NetworkType;
			}
			var net = conf.GetOrDefault<bool>("regtest", false) ? NetworkType.Regtest :
						conf.GetOrDefault<bool>("testnet", false) ? NetworkType.Testnet : NetworkType.Mainnet;

			return net;
		}

		protected override string GetDefaultConfigurationFileTemplate(IConfiguration conf)
		{
			var settings = GetDefaultSettings(conf);
			var networkType = GetNetworkType(conf);
			StringBuilder builder = new StringBuilder();
			builder.AppendLine("####Common Commands####");
			builder.AppendLine("####If Bitcoin Core is running with default settings, you should not need to modify this file####");
			builder.AppendLine("####All those options can be passed by through command like arguments (ie `-port=19382`)####");


			foreach(var network in new NBXplorerNetworkProvider(networkType).GetAll())
			{
				var cryptoCode = network.CryptoCode.ToLowerInvariant();
				builder.AppendLine("## This is the RPC Connection to your node");
				builder.AppendLine($"#{cryptoCode}.rpc.url=http://127.0.0.1:" + network.NBitcoinNetwork.RPCPort + "/");
				builder.AppendLine("#By user name and password");
				builder.AppendLine($"#{cryptoCode}.rpc.user=bitcoinuser");
				builder.AppendLine($"#{cryptoCode}.rpc.password=bitcoinpassword");
				builder.AppendLine("#By cookie file");
				builder.AppendLine($"#{cryptoCode}.rpc.cookiefile=yourbitcoinfolder/.cookie");
				builder.AppendLine("#By raw authentication string");
				builder.AppendLine($"#{cryptoCode}.rpc.auth=walletuser:password");
				builder.AppendLine();
				builder.AppendLine("## This is the connection to your node through P2P");
				builder.AppendLine($"#{cryptoCode}.node.endpoint=127.0.0.1:" + network);
				builder.AppendLine();
				builder.AppendLine("## startheight defines from which block you will start scanning, if -1 is set, it will use current blockchain height");
				builder.AppendLine($"#{cryptoCode}.startheight=-1");
				builder.AppendLine("## rescan forces a rescan from startheight");
				builder.AppendLine($"#{cryptoCode}.rescan=0");
			}
			builder.AppendLine("## Disable cookie, local ip authorization (unsecured)");
			builder.AppendLine("#noauth=0");
			builder.AppendLine("## What crypto currencies is supported");
			var chains = string.Join(',', new NBXplorerNetworkProvider(NetworkType.Mainnet)
				.GetAll()
				.Select(c => c.CryptoCode.ToLowerInvariant())
				.ToArray());
			builder.AppendLine($"#chains={chains}");
			builder.AppendLine("## Activate or disable verbose logs");
			builder.AppendLine("#verbose=0");

			builder.AppendLine();
			builder.AppendLine();

			builder.AppendLine("####Server Commands####");
			builder.AppendLine("#port=" + settings.DefaultPort);
			builder.AppendLine("#bind=127.0.0.1");
			builder.AppendLine($"#{networkType.ToString().ToLowerInvariant()}=1");
			builder.AppendLine();
			builder.AppendLine();
			builder.AppendLine("####Azure Service Bus####");
			builder.AppendLine("## Azure Service Bus configuration - set connection string to use Service Bus. Set Queue and / or Topic names to publish message to queues / topics");
			builder.AppendLine("#asbcnstr=Endpoint=sb://<yourdomain>.servicebus.windows.net/;SharedAccessKeyName=<your key name here>;SharedAccessKey=<your key here>");
			builder.AppendLine("#asbblockq=<new block queue name>");
			builder.AppendLine("#asbtranq=<new transaction queue name>");
			builder.AppendLine("#asbblockt=<new block topic name>");
			builder.AppendLine("#asbtrant=<new transaction topic name>");

			return builder.ToString();
		}

		private NBXplorerDefaultSettings GetDefaultSettings(IConfiguration conf)
		{
			return NBXplorerDefaultSettings.GetDefaultSettings(GetNetworkType(conf));
		}

		protected override IPEndPoint GetDefaultEndpoint(IConfiguration conf)
		{
			return new IPEndPoint(IPAddress.Parse("127.0.0.1"), GetDefaultSettings(conf).DefaultPort);
		}
	}
}
