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

		public ExplorerConfiguration LoadArgs(String[] args)
		{
			ConfigurationFile = args.Where(a => a.StartsWith("-conf=", StringComparison.Ordinal)).Select(a => a.Substring("-conf=".Length).Replace("\"", "")).FirstOrDefault();
			DataDir = args.Where(a => a.StartsWith("-datadir=", StringComparison.Ordinal)).Select(a => a.Substring("-datadir=".Length).Replace("\"", "")).FirstOrDefault();
			if(DataDir != null && ConfigurationFile != null)
			{
				var isRelativePath = Path.GetFullPath(ConfigurationFile).Length > ConfigurationFile.Length;
				if(isRelativePath)
				{
					ConfigurationFile = Path.Combine(DataDir, ConfigurationFile);
				}
			}

			Network = args.Contains("-testnet", StringComparer.OrdinalIgnoreCase) ? Network.TestNet :
				args.Contains("-regtest", StringComparer.OrdinalIgnoreCase) ? Network.RegTest :
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

			var consoleConfig = new TextFileConfiguration(args);
			var config = TextFileConfiguration.Parse(File.ReadAllText(ConfigurationFile));
			consoleConfig.MergeInto(config, true);

			Logs.Configuration.LogInformation("Network: " + Network);
			Logs.Configuration.LogInformation("Data directory set to " + DataDir);
			Logs.Configuration.LogInformation("Configuration file set to " + ConfigurationFile);

			Rescan = config.GetOrDefault<bool>("rescan", false);
			var defaultPort = config.GetOrDefault<int>("port", 37123);
			Listen = config
						.GetAll("bind")
						.Select(p => ConvertToEndpoint(p, defaultPort))
						.ToList();
			if(Listen.Count == 0)
			{
				Listen.Add(new IPEndPoint(IPAddress.Any, defaultPort));
			}

			RPC = RPCArgs.Parse(config, Network);
			NodeEndpoint = ConvertToEndpoint(config.GetOrDefault<string>("node.endpoint", "127.0.0.1"), Network.DefaultPort);
			CacheChain = config.GetOrDefault<bool>("cachechain", true);
			StartHeight = config.GetOrDefault<int>("startheight", 0);
			return this;
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
				builder.AppendLine("#Connection to the node instance");
				builder.AppendLine("#rpc.url=http://localhost:" + Network.RPCPort + "/");
				builder.AppendLine("#rpc.user=bitcoinuser");
				builder.AppendLine("#rpc.password=bitcoinpassword");
				builder.AppendLine("#rpc.cookiefile=yourbitcoinfolder/.cookie");
				builder.AppendLine("#node.endpoint=localhost:" + Network.DefaultPort);

				builder.AppendLine();
				builder.AppendLine();

				builder.AppendLine("####Server Commands####");
				builder.AppendLine("#port=37123");
				builder.AppendLine("#listen=0.0.0.0");
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
