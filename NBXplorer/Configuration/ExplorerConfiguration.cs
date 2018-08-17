using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NBitcoin.RPC;
using NBXplorer.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

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
		public int? PruneBeforeHeight
		{
			get;
			internal set;
		}

		public int? PruneKeepOnly
		{
			get;
			internal set;
		}

		public bool EnablePruning
		{
			get
			{
				return (PruneBeforeHeight.HasValue && PruneBeforeHeight.Value != -1) ||
					   (PruneKeepOnly.HasValue && PruneKeepOnly.Value != -1);
			}
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

		public int MinGapSize
		{
			get; set;
		} = 20;

		public int MaxGapSize
		{
			get; set;
		} = 30;

		public List<ChainConfiguration> ChainConfigurations
		{
			get; set;
		} = new List<ChainConfiguration>();

		public ExplorerConfiguration LoadArgs(IConfiguration config)
		{
			NetworkProvider = new NBXplorerNetworkProvider(DefaultConfiguration.GetNetworkType(config));
			var defaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(NetworkProvider.NetworkType);
			BaseDataDir = config.GetOrDefault<string>("datadir", null);
			if(BaseDataDir == null)
			{
				BaseDataDir = Path.GetDirectoryName(defaultSettings.DefaultDataDirectory);
				if(!Directory.Exists(BaseDataDir))
					Directory.CreateDirectory(BaseDataDir);
				if(!Directory.Exists(defaultSettings.DefaultDataDirectory))
					Directory.CreateDirectory(defaultSettings.DefaultDataDirectory);
			}

			Logs.Configuration.LogInformation("Network: " + NetworkProvider.NetworkType.ToString());
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
					var height = config.GetOrDefault<int>($"{network.CryptoCode}.prunebeforeheight", -1);
					if(height != -1)
						chainConfiguration.PruneBeforeHeight = height;

					var blocks = config.GetOrDefault<int>($"{network.CryptoCode}.prunekeeponly", -1);
					if(blocks != -1)
					{
						chainConfiguration.PruneKeepOnly = blocks;
						if(blocks < network.MinBlocksToKeep)
						{
							Logs.Configuration.LogWarning($"{network.CryptoCode}.prunekeeponly value is lower than the minimum block to keep for a pruned full node ({network.MinBlocksToKeep}). {network.MinBlocksToKeep} will be used instead of {blocks}.");
							chainConfiguration.PruneKeepOnly = network.MinBlocksToKeep;
						}
					}

					var args = RPCArgs.Parse(config, network.NBitcoinNetwork, network.CryptoCode);
					chainConfiguration.RPC = args.ConfigureRPCClient(network);
					if((chainConfiguration.RPC.CredentialString.CookieFile != null || chainConfiguration.RPC.CredentialString.UseDefault) && !network.SupportCookieAuthentication)
					{
						throw new ConfigException($"Chain {network.CryptoCode} does not support cookie file authentication,\n" +
							$"Please use {network.CryptoCode.ToLowerInvariant()}rpcuser and {network.CryptoCode.ToLowerInvariant()}rpcpassword settings in NBXplorer" +
							$"And configure rpcuser and rpcpassword in the configuration file or in commandline or your node");
					}
					chainConfiguration.NodeEndpoint = DefaultConfiguration.ConvertToEndpoint(config.GetOrDefault<string>($"{network.CryptoCode}.node.endpoint", "127.0.0.1"), network.NBitcoinNetwork.DefaultPort);
					chainConfiguration.StartHeight = config.GetOrDefault<int>($"{network.CryptoCode}.startheight", -1);

					ChainConfigurations.Add(chainConfiguration);
				}
			}
			var invalidChains = String.Join(',', supportedChains.Where(s => !validChains.Contains(s)).ToArray());
			if(!string.IsNullOrEmpty(invalidChains))
				throw new ConfigException($"Invalid chains {invalidChains}");

			Logs.Configuration.LogInformation("Supported chains: " + String.Join(',', supportedChains.ToArray()));
			MinGapSize = config.GetOrDefault<int>("mingapsize", 20);
			MaxGapSize = config.GetOrDefault<int>("maxgapsize", 30);
			if(MinGapSize >= MaxGapSize)
				throw new ConfigException("mingapsize should be equal or lower than maxgapsize");
			if(!Directory.Exists(BaseDataDir))
				Directory.CreateDirectory(BaseDataDir);
			DataDir = Path.Combine(BaseDataDir, NBXplorerDefaultSettings.GetFolderName(NetworkProvider.NetworkType));
			if(!Directory.Exists(DataDir))
				Directory.CreateDirectory(DataDir);
			CacheChain = config.GetOrDefault<bool>("cachechain", true);
			NoAuthentication = config.GetOrDefault<bool>("noauth", false);

			AzureServiceBusBlockQueue = config.GetOrDefault<string>("asbblockq", "newblock");
			AzureServiceBusTransactionQueue = config.GetOrDefault<string>("asbtranq", "newtransaction");
			AzureServiceBusConnectionString = config.GetOrDefault<string>("asbcnstr", "");

			return this;
		}

		public bool Supports(NBXplorerNetwork network)
		{
			return ChainConfigurations.Any(c => network.CryptoCode == c.CryptoCode);
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

		public string AzureServiceBusConnectionString
		{
			get;
			set;
		}

		public string AzureServiceBusBlockQueue
		{
			get;
			set;
		}

		public string AzureServiceBusTransactionQueue
		{
			get;
			set;
		}
	}
}
