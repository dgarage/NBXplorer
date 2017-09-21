using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.CommandLineUtils;
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

namespace NBXplorer.Configuration
{
	public class DefaultConfiguration
	{
		public static IConfiguration CreateConfiguration(string[] args)
		{
			CommandLineApplication app = new CommandLineApplication(true)
			{
				FullName = "NBXplorer\r\nLightweight block explorer for tracking HD wallets",
				Name = "NBXplorer"
			};
			app.HelpOption("-? | -h | --help");
			app.Option("-n | --network", $"Set the network among ({NetworkInformation.ToStringAll()}) (default: {Network.Main.ToString()})", CommandOptionType.SingleValue);
			app.Option("-d | --datadir", $"The data directory (default: depends on network)", CommandOptionType.SingleValue);
			app.Option("--rpcuser", $"The RPC user (default: using cookie auth from default network folder)", CommandOptionType.SingleValue);
			app.Option("--rpcpassword", $"The RPC password (default: using cookie auth from default network folder)", CommandOptionType.SingleValue);
			app.Option("--rpccookiefile", $"The RPC cookiefile (default: using cookie auth from default network folder)", CommandOptionType.SingleValue);
			app.Option("--rpcurl", $"The RPC server url (default: default rpc server depended on the network)", CommandOptionType.SingleValue);
			app.Option("--rpcnotest", $"Faster start because RPC connection testing skipped (default: false)", CommandOptionType.SingleValue);
			app.Option("--startheight", $"The height where starting the scan (default: where your rpc server was synched when you first started this program)", CommandOptionType.SingleValue);
			app.Option("--nodeendpoint", $"The p2p connection to a Bitcoin node, make sure you are whitelisted (default: default p2p node on localhost, depends on network)", CommandOptionType.SingleValue);
			var binds = app.Option("-b | --bind", $"The address on which to bind (default: 127.0.0.1)", CommandOptionType.MultipleValue);
			app.Option("-c | --conf", $"The configuration file (default: depends on network)", CommandOptionType.SingleValue);
			app.Option("-p | --port", $"The port on which to listen (default: depending on the network)", CommandOptionType.SingleValue);

			try
			{
				bool executed = false;
				app.OnExecute(() =>
				{
					executed = true;
					return 1;
				});
				app.Execute(args);

				if(!executed)
					throw new ConfigException();
			}
			catch(Exception ex)
			{
				Console.WriteLine(ex.Message);
				app.ShowHelp();
				throw new ConfigException();
			}

			var commandLineArgs = app.Options.Where(o => o.Value() != null).Select(o => new KeyValuePair<string, string>(o.LongName, o.Value())).ToArray();

			var conf = new ConfigurationBuilder()
				.AddEnvironmentVariables()
				.AddInMemoryCollection(commandLineArgs)
				.Build();

			var network = NetworkInformation.GetNetworkByName(conf.GetOrDefault<string>("network", NetworkInformation.Main.Network.Name));
			if(network == null)
				throw new ConfigException($"Invalid network name {conf.GetOrDefault<string>("network", null)}");

			var datadir = conf.GetValue<string>("datadir", network.DefaultDataDirectory);
			if(!Directory.Exists(datadir))
				Directory.CreateDirectory(datadir);

			var confFile = conf.GetValue<string>("conf", network.DefaultConfigurationFile);
			Logs.Configuration.LogInformation($"Configuration File: " + Path.GetFullPath(confFile));

			EnsureConfigFileExists(confFile, network);


			var finalConf = new ConfigurationBuilder()
				.AddEnvironmentVariables()
				.AddInMemoryCollection(new[]
				{
					new KeyValuePair<string, string>("conf", network.DefaultConfigurationFile),
					new KeyValuePair<string, string>("datadir", network.DefaultDataDirectory)
				})
				.AddIniFile(confFile)
				.AddInMemoryCollection(commandLineArgs)
				.Build();

			List<KeyValuePair<string, string>> additionalSettings = new List<KeyValuePair<string, string>>();
			if(binds.HasValue() || finalConf["port"] != null)
			{
				var defaultPort = finalConf.GetOrDefault<int>("port", network.DefaultExplorerPort);
				var listen = binds.Values
							.Select(p => ConvertToEndpoint(p, defaultPort))
							.OfType<object>()
							.ToArray();

				if(listen.Length == 0)
					listen = new object[] { "127.0.0.1:" + defaultPort };

				finalConf = new ConfigurationBuilder()
				.AddEnvironmentVariables()
				.AddInMemoryCollection(new[]
				{
					new KeyValuePair<string, string>("conf", network.DefaultConfigurationFile),
					new KeyValuePair<string, string>("datadir", network.DefaultDataDirectory),
					new KeyValuePair<string, string>(WebHostDefaults.ServerUrlsKey, string.Join(";", listen.Select(l=>$"http://{l}/"))),
				})
				.AddIniFile(confFile)
				.AddInMemoryCollection(commandLineArgs)
				.Build();

			}

			return finalConf;
		}

		public static IPEndPoint ConvertToEndpoint(string str, int defaultPort)
		{
			var portOut = defaultPort;
			var hostOut = "";
			int colon = str.LastIndexOf(':');
			// if a : is found, and it either follows a [...], or no other : is in the string, treat it as port separator
			bool fHaveColon = colon != -1;
			bool fBracketed = fHaveColon && (str[0] == '[' && str[colon - 1] == ']'); // if there is a colon, and in[0]=='[', colon is not 0, so in[colon-1] is safe
			bool fMultiColon = fHaveColon && (str.LastIndexOf(':', colon - 1) != -1);
			if(fHaveColon && (colon == 0 || fBracketed || !fMultiColon))
			{
				int n;
				if(int.TryParse(str.Substring(colon + 1), out n) && n > 0 && n < 0x10000)
				{
					str = str.Substring(0, colon);
					portOut = n;
				}
			}
			if(str.Length > 0 && str[0] == '[' && str[str.Length - 1] == ']')
				hostOut = str.Substring(1, str.Length - 2);
			else
				hostOut = str;

			IPAddress ip = null;

			if(!IPAddress.TryParse(hostOut, out ip))
			{
				ip = Dns.GetHostEntry(hostOut).AddressList.FirstOrDefault();
				if(ip == null)
					throw new FormatException("Invalid IP Endpoint");
			}

			return new IPEndPoint(ip, portOut);
		}

		private static void EnsureConfigFileExists(string confFile, NetworkInformation network)
		{
			if(!File.Exists(confFile))
			{
				Logs.Configuration.LogInformation("Creating configuration file");
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
				File.WriteAllText(confFile, builder.ToString());
			}
		}
	}
}
