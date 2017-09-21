using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using NBXplorer.Configuration;
using NBXplorer.Logging;
using NBitcoin.Protocol;
using System.Collections;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore;
using NBitcoin;
using System.Text;

namespace NBXplorer
{
	public class Program
	{
		public static void Main(string[] args)
		{
			Logs.Configure(new FuncLoggerFactory(i => new CustomerConsoleLogger(i, (a, b) => true, false)));
			IWebHost host = null;
			try
			{
				ConfigurationBuilder builder = new ConfigurationBuilder();
				host = new WebHostBuilder()
					.UseKestrel()
					.UseIISIntegration()
					.UseConfiguration(CreateConfiguration(args))
					.UseStartup<Startup>()
					.UseUrls()
					.Build();
				host.Run();
			}
			catch(ConfigException ex)
			{
				if(!string.IsNullOrEmpty(ex.Message))
					Logs.Configuration.LogError(ex.Message);
			}
			catch(Exception exception)
			{
				Logs.Explorer.LogError("Exception thrown while running the server");
				Logs.Explorer.LogError(exception.ToString());
			}
			finally
			{
				if(host != null)
					host.Dispose();
			}
		}

		public static IConfiguration CreateConfiguration(string[] args)
		{
			var conf = new ConfigurationBuilder()
				.AddEnvironmentVariables()
				.AddCommandLine(args)
				.Build();

			var network = NetworkInformation.GetNetworkByName(conf.GetOrDefault<string>("network", NetworkInformation.Main.Network.Name));
			if(network == null)
				throw new ConfigurationException($"Invalid network name {conf.GetOrDefault<string>("network", null)}");

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
				.AddCommandLine(args)
				.Build();

			return finalConf;
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
