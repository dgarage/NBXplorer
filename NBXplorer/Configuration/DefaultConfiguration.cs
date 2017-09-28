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
			CommandLineApplication app = new CommandLineApplication(true)
			{
				FullName = "NBXplorer\r\nLightweight block explorer for tracking HD wallets",
				Name = "NBXplorer"
			};
			app.HelpOption("-? | -h | --help");
			app.Option("-n | --network", $"Set the network among ({NetworkInformation.ToStringAll()}) (default: {Network.Main.ToString()})", CommandOptionType.SingleValue);
			app.Option("--testnet | -testnet", $"Use testnet", CommandOptionType.BoolValue);
			app.Option("--regtest | -regtest", $"Use regtest", CommandOptionType.BoolValue);
			app.Option("--rescan", $"Rescan from startheight", CommandOptionType.BoolValue);
			app.Option("--noauth", $"Disable cookie authentication", CommandOptionType.BoolValue);
			app.Option("--rpcuser", $"The RPC user (default: using cookie auth from default network folder)", CommandOptionType.SingleValue);
			app.Option("--rpcpassword", $"The RPC password (default: using cookie auth from default network folder)", CommandOptionType.SingleValue);
			app.Option("--rpccookiefile", $"The RPC cookiefile (default: using cookie auth from default network folder)", CommandOptionType.SingleValue);
			app.Option("--rpcurl", $"The RPC server url (default: default rpc server depended on the network)", CommandOptionType.SingleValue);
			app.Option("--rpcnotest", $"Faster start because RPC connection testing skipped (default: false)", CommandOptionType.SingleValue);
			app.Option("--startheight", $"The height where starting the scan (default: where your rpc server was synched when you first started this program)", CommandOptionType.SingleValue);
			app.Option("--nodeendpoint", $"The p2p connection to a Bitcoin node, make sure you are whitelisted (default: default p2p node on localhost, depends on network)", CommandOptionType.SingleValue);
			return app;
		}


		public override string EnvironmentVariablePrefix => "NBXPLORER_";
		protected override string GetDefaultDataDir(IConfiguration conf)
		{
			return GetNetwork(conf).DefaultDataDirectory;
		}

		protected override string GetDefaultConfigurationFile(IConfiguration conf)
		{
			var network = GetNetwork(conf);
			var dataDir = conf["datadir"];
			if(dataDir == null)
				return network.DefaultConfigurationFile;
			var fileName = Path.GetFileName(network.DefaultConfigurationFile);
			return Path.Combine(dataDir, fileName);
		}

		public static NetworkInformation GetNetwork(IConfiguration conf)
		{
			var network = conf.GetOrDefault<string>("network", null);
			if(network != null)
			{
				var info = NetworkInformation.GetNetworkByName(network);
				if(info == null)
					throw new ConfigException($"Invalid network name {network}");
				return info;
			}

			var net = conf.GetOrDefault<bool>("regtest", false) ? Network.RegTest :
						conf.GetOrDefault<bool>("testnet", false) ? Network.TestNet : Network.Main;

			return NetworkInformation.GetNetworkByName(net.Name);
		}

		protected override string GetDefaultConfigurationFileTemplate(IConfiguration conf)
		{
			var network = GetNetwork(conf);
			StringBuilder builder = new StringBuilder();
			builder.AppendLine("####Common Commands####");
			builder.AppendLine("####If Bitcoin Core is running with default settings, you should not need to modify this file####");
			builder.AppendLine("####All those options can be passed by through command like arguments (ie `-port=19382`)####");

			builder.AppendLine("## This is the RPC Connection to your node");
			builder.AppendLine("#rpc.url=http://localhost:" + network.Network.RPCPort + "/");
			builder.AppendLine("#By user name and password");
			builder.AppendLine("#rpc.user=bitcoinuser");
			builder.AppendLine("#rpc.password=bitcoinpassword");
			builder.AppendLine("#By cookie file");
			builder.AppendLine("#rpc.cookiefile=yourbitcoinfolder/.cookie");
			builder.AppendLine("#By raw authentication string");
			builder.AppendLine("#rpc.auth=walletuser:password");
			builder.AppendLine();
			builder.AppendLine("## This is the connection to your node through P2P");
			builder.AppendLine("#node.endpoint=127.0.0.1:" + network);
			builder.AppendLine();
			builder.AppendLine("## startheight defines from which block you will start scanning, if -1 is set, it will use current blockchain height");
			builder.AppendLine("#startheight=-1");
			builder.AppendLine("## rescan forces a rescan from startheight");
			builder.AppendLine("#rescan=0");
			builder.AppendLine("## Disable cookie, local ip authorization (unsecured)");
			builder.AppendLine("#noauth=0");

			builder.AppendLine();
			builder.AppendLine();

			builder.AppendLine("####Server Commands####");
			builder.AppendLine("#port=" + network.DefaultExplorerPort);
			builder.AppendLine("#bind=127.0.0.1");
			builder.AppendLine("#testnet=0");
			builder.AppendLine("#regtest=0");
			return builder.ToString();
		}

		protected override IPEndPoint GetDefaultEndpoint(IConfiguration conf)
		{
			return new IPEndPoint(IPAddress.Parse("127.0.0.1"), GetNetwork(conf).DefaultExplorerPort);
		}
	}
}
