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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Ini;

namespace NBXplorer.Configuration
{
	public class ChainConfiguration
	{
		public bool Rescan
		{
			get;
			set;
		}
		public RPCClient RPC
		{
			get;
			internal set;
		}
		public IPEndPoint NodeEndpoint
		{
			get;
			internal set;
		}
		public int StartHeight
		{
			get;
			internal set;
		}
		public string CryptoCode
		{
			get;
			set;
		}
	}
	public class ExplorerConfiguration
	{
		public string ConfigurationFile
		{
			get;
			set;
		}
		public string BaseDataDir
		{
			get;
			set;
		}

		public string DataDir
		{
			get; set;
		}

		public NBXplorerNetworkProvider NetworkProvider
		{
			get; set;
		}
		public RPCArgs RPC
		{
			get;
			set;
		}

		public bool Rescan
		{
			get; set;
		}

		public List<ChainConfiguration> ChainConfigurations
		{
			get; set;
		} = new List<ChainConfiguration>();

		public ExplorerConfiguration LoadArgs(IConfiguration config)
		{
			NetworkProvider = new NBXplorerNetworkProvider(DefaultConfiguration.GetChainType(config));
			var defaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(NetworkProvider.ChainType);

			Logs.Configuration.LogInformation("Network: " + NetworkProvider.ChainType.ToString());
			var supportedChains = config.GetOrDefault<string>("chains", "btc")
									  .Split(',', StringSplitOptions.RemoveEmptyEntries)
									  .Select(t => t.ToUpperInvariant());
			var validChains = new List<string>();
			foreach(var network in NetworkProvider.GetAll())
			{
				if(supportedChains.Contains(network.CryptoCode))
				{
					validChains.Add(network.CryptoCode);
					var chainConfiguration = new ChainConfiguration();
					chainConfiguration.Rescan = config.GetOrDefault<bool>($"{network.CryptoCode}.rescan", false);
					chainConfiguration.CryptoCode = network.CryptoCode;
					var args = RPCArgs.Parse(config, network.NBitcoinNetwork, network.CryptoCode);
					chainConfiguration.RPC = args.ConfigureRPCClient(network);
					chainConfiguration.NodeEndpoint = DefaultConfiguration.ConvertToEndpoint(config.GetOrDefault<string>($"{network.CryptoCode}.node.endpoint", "127.0.0.1"), network.NBitcoinNetwork.DefaultPort);
					chainConfiguration.StartHeight = config.GetOrDefault<int>($"{network.CryptoCode}.startheight", -1);

					ChainConfigurations.Add(chainConfiguration);
				}
			}
			var invalidChains = String.Join(',', supportedChains.Where(s => !validChains.Contains(s)).ToArray());
			if(!string.IsNullOrEmpty(invalidChains))
				throw new ConfigException($"Invalid chains {invalidChains}");

			Logs.Configuration.LogInformation("Supported chains: " + String.Join(',', supportedChains.ToArray()));
			BaseDataDir = config.GetOrDefault<string>("datadir", Path.GetDirectoryName(defaultSettings.DefaultDataDirectory));
			if(!Directory.Exists(BaseDataDir))
				Directory.CreateDirectory(BaseDataDir);
			DataDir = Path.Combine(BaseDataDir, NetworkProvider.ChainType.ToNetwork().Name);
			if(!Directory.Exists(DataDir))
				Directory.CreateDirectory(DataDir);
			CacheChain = config.GetOrDefault<bool>("cachechain", true);
			NoAuthentication = config.GetOrDefault<bool>("noauth", false);
			return this;
		}

		public bool Supports(NBXplorerNetwork network)
		{
			return ChainConfigurations.Any(c => network.CryptoCode == c.CryptoCode);
		}

		public int StartHeight
		{
			get; set;
		}
		
		public bool CacheChain
		{
			get;
			set;
		}
		public bool NoAuthentication
		{
			get;
			set;
		}
	}
}
