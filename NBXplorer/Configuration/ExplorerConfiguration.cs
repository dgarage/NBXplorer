using System;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using System.IO;
using System.Net;
using NBXplorer.Logging;
using NBitcoin.Protocol;
using NBitcoin.DataEncoders;
using NBitcoin.RPC;

namespace NBXplorer.Configuration
{
	public class ExplorerConfiguration
	{
		public string ConfigurationFile
		{
			get;
			set;
		}
		public string DataDir
		{
			get;
			set;
		}

		public Network Network
		{
			get; set;
		}
		public List<IPEndPoint> Listen
		{
			get;
			set;
		} = new List<IPEndPoint>();
		public RPCArgs RPC
		{
			get;
			set;
		}

		public bool Rescan
		{
			get; set;
		}

		public void LoadArgs(string[] args)
		{
			LoadArgs(new TextFileConfiguration(args));
		}

		public ExplorerConfiguration LoadArgs(TextFileConfiguration consoleConfig)
		{
			ConfigurationFile = consoleConfig.GetOrDefault<string>("conf", null);
			DataDir = consoleConfig.GetOrDefault<string>("datadir", null);
			if(DataDir != null && ConfigurationFile != null)
			{
				var isRelativePath = Path.GetFullPath(ConfigurationFile).Length > ConfigurationFile.Length;
				if(isRelativePath)
				{
					ConfigurationFile = Path.Combine(DataDir, ConfigurationFile);
				}
			}

			Network = consoleConfig.GetOrDefault<bool>("testnet", false) ? Network.TestNet :
				consoleConfig.GetOrDefault<bool>("regtest", false) ? Network.RegTest :
				Network.Main;

			if(ConfigurationFile != null)
			{
				AssetConfigFileExists();
				var configTemp = TextFileConfiguration.Parse(File.ReadAllText(ConfigurationFile));
				Network = configTemp.GetOrDefault<bool>("testnet", false) ? Network.TestNet :
						  configTemp.GetOrDefault<bool>("regtest", false) ? Network.RegTest :
						  Network.Main;
			}
			if(DataDir == null)
			{
				DataDir = DefaultDataDirectory.GetDefaultDirectory("NBXplorer", Network);
			}

			if(ConfigurationFile == null)
			{
				ConfigurationFile = GetDefaultConfigurationFile();
			}

			if(!Directory.Exists(DataDir))
				throw new ConfigurationException("Data directory does not exists");

			var config = TextFileConfiguration.Parse(File.ReadAllText(ConfigurationFile));
			consoleConfig.MergeInto(config, true);

			Logs.Configuration.LogInformation("Network: " + Network);
			Logs.Configuration.LogInformation("Data directory set to " + DataDir);
			Logs.Configuration.LogInformation("Configuration file set to " + ConfigurationFile);

			Rescan = config.GetOrDefault<bool>("rescan", false);
			var defaultPort = config.GetOrDefault<int>("port", GetDefaultPort(Network));
			Listen = config
						.GetAll("bind")
						.Select(p => ConvertToEndpoint(p, defaultPort))
						.ToList();
			if(Listen.Count == 0)
			{
				Listen.Add(new IPEndPoint(IPAddress.Parse("127.0.0.1"), defaultPort));
			}

			RPC = RPCArgs.Parse(config, Network);
			NodeEndpoint = ConvertToEndpoint(config.GetOrDefault<string>("node.endpoint", "127.0.0.1"), Network.DefaultPort);
			CacheChain = config.GetOrDefault<bool>("cachechain", true);
			StartHeight = config.GetOrDefault<int>("startheight", -1);
			return this;
		}

		private int GetDefaultPort(Network network)
		{
			return network == Network.Main ? 24444 :
				network == Network.TestNet ? 24445 : 24446;
		}

		public Serializer CreateSerializer()
		{
			return new Serializer(Network);
		}

		public int StartHeight
		{
			get; set;
		}

		public string[] GetUrls()
		{
			return Listen.Select(b => "http://" + b + "/").ToArray();
		}

		public IPEndPoint NodeEndpoint
		{
			get; set;
		}
		public bool CacheChain
		{
			get;
			set;
		}

		public ExplorerRuntime CreateRuntime()
		{
			return new ExplorerRuntime(this);
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
			return new IPEndPoint(IPAddress.Parse(hostOut), portOut);
		}

		private void AssetConfigFileExists()
		{
			if(!File.Exists(ConfigurationFile))
				throw new ConfigurationException("Configuration file does not exists");
		}

		private string GetDefaultConfigurationFile()
		{
			var config = Path.Combine(DataDir, "settings.config");
			Logs.Configuration.LogInformation("Configuration file set to " + config);
			if(!File.Exists(config))
			{
				Logs.Configuration.LogInformation("Creating configuration file");
				StringBuilder builder = new StringBuilder();
				builder.AppendLine("####Common Commands####");
				builder.AppendLine("####If Bitcoin Core is running with default settings, you should not need to modify this file####");
				builder.AppendLine("####All those options can be passed by through command like arguments (ie `-port=19382`)####");

				builder.AppendLine("## This is the RPC Connection to your node");
				builder.AppendLine("#rpc.url=http://localhost:" + Network.RPCPort + "/");
				builder.AppendLine("#By user name and password");
				builder.AppendLine("#rpc.user=bitcoinuser");
				builder.AppendLine("#rpc.password=bitcoinpassword");
				builder.AppendLine("#By cookie file");
				builder.AppendLine("#rpc.cookiefile=yourbitcoinfolder/.cookie");
				builder.AppendLine("#By raw authentication string");
				builder.AppendLine("#rpc.auth=walletuser:password");
				builder.AppendLine();
				builder.AppendLine("## This is the connection to your node through P2P");
				builder.AppendLine("#node.endpoint=localhost:" + Network.DefaultPort);
				builder.AppendLine();
				builder.AppendLine("## startheight defines from which block you will start scanning, if -1 is set, it will use current blockchain height");
				builder.AppendLine("#startheight=-1");
				builder.AppendLine("## rescan forces a rescan from startheight");
				builder.AppendLine("#rescan=0");

				builder.AppendLine();
				builder.AppendLine();

				builder.AppendLine("####Server Commands####");
				builder.AppendLine("#port=" + GetDefaultPort(Network));
				builder.AppendLine("#bind=127.0.0.1");
				builder.AppendLine("#testnet=0");
				builder.AppendLine("#regtest=0");
				File.WriteAllText(config, builder.ToString());
			}
			return config;
		}
	}

	public class ConfigException : Exception
	{
		public ConfigException() : base("")
		{

		}
		public ConfigException(string message) : base(message)
		{

		}
	}
}
