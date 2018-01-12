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
			var provider = new NBXplorerNetworkProvider(ChainType.Main);
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
				app.Option($"--{crypto}rpcuser", $"The RPC user (default: using cookie auth from default network folder)", CommandOptionType.SingleValue);
				app.Option($"--{crypto}rescan", $"Rescan from startheight", CommandOptionType.BoolValue);
				app.Option($"--{crypto}rpcpassword", $"The RPC password (default: using cookie auth from default network folder)", CommandOptionType.SingleValue);
				app.Option($"--{crypto}rpccookiefile", $"The RPC cookiefile (default: using cookie auth from default network folder)", CommandOptionType.SingleValue);
				app.Option($"--{crypto}rpcurl", $"The RPC server url (default: default rpc server depended on the network)", CommandOptionType.SingleValue);
				app.Option($"--{crypto}startheight", $"The height where starting the scan (default: where your rpc server was synched when you first started this program)", CommandOptionType.SingleValue);
				app.Option($"--{crypto}nodeendpoint", $"The p2p connection to a Bitcoin node, make sure you are whitelisted (default: default p2p node on localhost, depends on network)", CommandOptionType.SingleValue);
			}
				
			app.Option("--noauth", $"Disable cookie authentication", CommandOptionType.BoolValue);
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
			return Path.Combine(dataDir, fileName);
		}

		public static ChainType GetChainType(IConfiguration conf)
		{
			var network = conf.GetOrDefault<string>("network", null);
			if(network != null)
			{
				var n = Network.GetNetwork(network);
				if(n == Network.Main)
					return ChainType.Main;
				if(n == Network.TestNet)
					return ChainType.Test;
				if(n == Network.RegTest)
					return ChainType.Regtest;
				throw new ConfigException("invalid network " + network);
			}
			var net = conf.GetOrDefault<bool>("regtest", false) ? ChainType.Regtest :
						conf.GetOrDefault<bool>("testnet", false) ? ChainType.Test : ChainType.Main;

			return net;
		}

		protected override string GetDefaultConfigurationFileTemplate(IConfiguration conf)
		{
			var settings = GetDefaultSettings(conf);
			StringBuilder builder = new StringBuilder();
			builder.AppendLine("####Common Commands####");
			builder.AppendLine("####If Bitcoin Core is running with default settings, you should not need to modify this file####");
			builder.AppendLine("####All those options can be passed by through command like arguments (ie `-port=19382`)####");

			
			foreach(var network in new NBXplorerNetworkProvider(settings.ChainType).GetAll())
			{
				builder.AppendLine("## This is the RPC Connection to your node");
				builder.AppendLine($"#{network.CryptoCode}.rpc.url=http://127.0.0.1:" + network.NBitcoinNetwork.RPCPort + "/");
				builder.AppendLine("#By user name and password");
				builder.AppendLine($"#{network.CryptoCode}.rpc.user=bitcoinuser");
				builder.AppendLine($"#{network.CryptoCode}.rpc.password=bitcoinpassword");
				builder.AppendLine("#By cookie file");
				builder.AppendLine($"#{network.CryptoCode}.rpc.cookiefile=yourbitcoinfolder/.cookie");
				builder.AppendLine("#By raw authentication string");
				builder.AppendLine($"#{network.CryptoCode}.rpc.auth=walletuser:password");
				builder.AppendLine();
				builder.AppendLine("## This is the connection to your node through P2P");
				builder.AppendLine($"#{network.CryptoCode}.node.endpoint=127.0.0.1:" + network);
				builder.AppendLine();
				builder.AppendLine("## startheight defines from which block you will start scanning, if -1 is set, it will use current blockchain height");
				builder.AppendLine($"#{network.CryptoCode}.startheight=-1");
				builder.AppendLine("## rescan forces a rescan from startheight");
				builder.AppendLine($"#{network.CryptoCode}.rescan=0");
			}
			builder.AppendLine("## Disable cookie, local ip authorization (unsecured)");
			builder.AppendLine("#noauth=0");
			builder.AppendLine("## What crypto currencies is supported");
			builder.AppendLine("#chains=btc,ltc");
			builder.AppendLine("## Activate or disable verbose logs");
			builder.AppendLine("#verbose=0");

			builder.AppendLine();
			builder.AppendLine();

			builder.AppendLine("####Server Commands####");
			builder.AppendLine("#port=" + settings.DefaultPort);
			builder.AppendLine("#bind=127.0.0.1");
			builder.AppendLine("#testnet=0");
			builder.AppendLine("#regtest=0");
			return builder.ToString();
		}

		private NBXplorerDefaultSettings GetDefaultSettings(IConfiguration conf)
		{
			return NBXplorerDefaultSettings.GetDefaultSettings(GetChainType(conf));
		}

		protected override IPEndPoint GetDefaultEndpoint(IConfiguration conf)
		{
			return new IPEndPoint(IPAddress.Parse("127.0.0.1"), GetDefaultSettings(conf).DefaultPort);
		}
	}
}
