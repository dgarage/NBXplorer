using System;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Collections.Generic;
using NBitcoin;
using System.IO;
using System.Net;
using NBXplorer.Logging;
using NBitcoin.RPC;
using Microsoft.Extensions.Configuration;

namespace NBXplorer.Configuration
{
	public class ChainConfiguration
	{
		public bool Rescan
		{
			get;
			set;
		}
		public DateTimeOffset? RescanIfTimeBefore
		{
			get;
			internal set;
		}
		public RPCClient RPC
		{
			get;
			internal set;
		}
		public EndPoint NodeEndpoint
		{
			get;
			internal set;
		}
		public int StartHeight
		{
			get;
			internal set;
		}
		public Money MinUtxoValue
		{
			get;
			internal set;
		}
		public string CryptoCode
		{
			get;
			set;
		}
		public bool NoWarmup { get; set; }
		public bool ExposeRPC { get; set; }
	}
	public class ExplorerConfiguration
	{
		public string BaseDataDir
		{
			get;
			set;
		}

		public string DataDir
		{
			get; set;
		}
		public string SignalFilesDir { get; set; }
		public NBXplorerNetworkProvider NetworkProvider
		{
			get; set;
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
			var exposeRPCGlobal = config.GetOrDefault<bool>("exposerpc", false);
			foreach(var network in NetworkProvider.GetAll())
			{
				if(supportedChains.Contains(network.CryptoCode))
				{
					validChains.Add(network.CryptoCode);
					var chainConfiguration = new ChainConfiguration();
					chainConfiguration.Rescan = config.GetOrDefault<bool>($"{network.CryptoCode}.rescan", false);
					var rescanIfTimeBefore = config.GetOrDefault<long>($"{network.CryptoCode}.rescaniftimebefore", 0);
					if (rescanIfTimeBefore != 0)
					{
						chainConfiguration.RescanIfTimeBefore = NBitcoin.Utils.UnixTimeToDateTime(rescanIfTimeBefore);
					}
					chainConfiguration.CryptoCode = network.CryptoCode;

					var args = RPCArgs.Parse(config, network.NBitcoinNetwork, network.CryptoCode);

					chainConfiguration.RPC = args.ConfigureRPCClient(network);
					if (chainConfiguration.RPC.Address.Port == network.NBitcoinNetwork.DefaultPort)
					{
						Logs.Configuration.LogWarning($"{network.CryptoCode}: It seems that the RPC port ({chainConfiguration.RPC.Address.Port}) is equal to the default P2P port ({network.NBitcoinNetwork.DefaultPort}), this is probably a misconfiguration.");
					}
					chainConfiguration.NodeEndpoint = NBitcoin.Utils.ParseEndpoint(config.GetOrDefault<string>($"{network.CryptoCode}.node.endpoint", "127.0.0.1"), network.NBitcoinNetwork.DefaultPort);

					if (GetPort(chainConfiguration.NodeEndpoint) == network.NBitcoinNetwork.RPCPort)
					{
						Logs.Configuration.LogWarning($"{network.CryptoCode}: It seems that the node endpoint port ({GetPort(chainConfiguration.NodeEndpoint)}) is equal to the default RPC port ({network.NBitcoinNetwork.RPCPort}), this is probably a misconfiguration.");
					}

					chainConfiguration.StartHeight = config.GetOrDefault<int>($"{network.CryptoCode}.startheight", -1);

					if (!(network is NBXplorer.NBXplorerNetworkProvider.LiquidNBXplorerNetwork))
					{
						if (config.GetOrDefault<int>($"{network.CryptoCode}.minutxovalue", -1) is int v && v != -1)
						{
							chainConfiguration.MinUtxoValue = Money.Satoshis(v);
						}
					}
					
					chainConfiguration.ExposeRPC = config.GetOrDefault<bool>($"{network.CryptoCode}.exposerpc", exposeRPCGlobal);
					chainConfiguration.NoWarmup = config.GetOrDefault<bool>($"nowarmup", false);
					ChainConfigurations.Add(chainConfiguration);
				}
			}
			var invalidChains = String.Join(',', supportedChains.Where(s => !validChains.Contains(s)).ToArray());
			if(!string.IsNullOrEmpty(invalidChains))
				throw new ConfigException($"Invalid chains {invalidChains} for {NetworkProvider.NetworkType}");
			SocksEndpoint = config.GetOrDefault<string>($"socks.endpoint", null) is string e ? NBitcoin.Utils.ParseEndpoint(e, 1080) : null;
			if (SocksEndpoint != null)
			{
				Logs.Configuration.LogInformation("Socks endpoint: " + SocksEndpoint.ToEndpointString());
				var user = config.GetOrDefault<string>("socks.user", null);
				var pass = config.GetOrDefault<string>("socks.password", null);
				if (user != null && pass != null)
				{
					Logs.Configuration.LogInformation("Socks credentials detected");
					SocksCredentials = new NetworkCredential(user, pass);
				}
			}

			Logs.Configuration.LogInformation("Supported chains: " + String.Join(',', supportedChains.ToArray()));
			MinGapSize = config.GetOrDefault<int>("mingapsize", 20);
			MaxGapSize = config.GetOrDefault<int>("maxgapsize", 30);
			if (MinGapSize >= MaxGapSize)
				throw new ConfigException("mingapsize should be equal or lower than maxgapsize");
			if(!Directory.Exists(BaseDataDir))
				Directory.CreateDirectory(BaseDataDir);
			DataDir = Path.Combine(BaseDataDir, NBXplorerDefaultSettings.GetFolderName(NetworkProvider.NetworkType));
			if (!Directory.Exists(DataDir))
				Directory.CreateDirectory(DataDir);
			SignalFilesDir = config.GetOrDefault<string>("signalfilesdir", null);
			SignalFilesDir = SignalFilesDir ?? DataDir;
			if (!Directory.Exists(SignalFilesDir))
				Directory.CreateDirectory(SignalFilesDir);
			NoAuthentication = config.GetOrDefault<bool>("noauth", false);
			InstanceName = config.GetOrDefault<string>("instancename", "");
			TrimEvents = config.GetOrDefault<int>("trimevents", -1);

			var customKeyPathTemplate = config.GetOrDefault<string>("customkeypathtemplate", null);
			if (!string.IsNullOrEmpty(customKeyPathTemplate))
			{
				if (!KeyPathTemplate.TryParse(customKeyPathTemplate, out var v))
					throw new ConfigException("Invalid customKeyPathTemplate");
				if (v.PostIndexes.IsHardened || v.PreIndexes.IsHardened)
					throw new ConfigException("customKeyPathTemplate should not be an hardened path");
				CustomKeyPathTemplate = v;
			}

			AzureServiceBusConnectionString = config.GetOrDefault<string>("asbcnstr", "");
			AzureServiceBusBlockQueue = config.GetOrDefault<string>("asbblockq", "");
			AzureServiceBusTransactionQueue = config.GetOrDefault<string>("asbtranq", "");
			AzureServiceBusBlockTopic = config.GetOrDefault<string>("asbblockt", "");
			AzureServiceBusTransactionTopic = config.GetOrDefault<string>("asbtrant", "");

			RabbitMqHostName = config.GetOrDefault<string>("rmqhost", "");
			RabbitMqVirtualHost = config.GetOrDefault<string>("rmqvirtual", "");
			RabbitMqUsername = config.GetOrDefault<string>("rmquser", "");
			RabbitMqPassword = config.GetOrDefault<string>("rmqpass", "");
			RabbitMqTransactionExchange = config.GetOrDefault<string>("rmqtranex", "");
			RabbitMqBlockExchange = config.GetOrDefault<string>("rmqblockex", "");

			var obsolete = string.Join(", ",
				new[] { "dbtrie", "automigrate", "nomigrateevts", "nomigraterawtxs", "cachechain", "deleteaftermigration", "dbcache" }
				.Where(o => !string.IsNullOrEmpty(config[o])));

			if (obsolete != string.Empty)
			{
				if (Directory.Exists(Path.Combine(DataDir, "db")))
					throw new ConfigException($"Options '{obsolete}' are not supported anymore, if you need to migrate an old instance to the new postgres backend, please use NBXplorer v2.5.2 and follow https://github.com/dgarage/NBXplorer/blob/master/docs/Postgres-Migration.md.");
				else
					Logs.Explorer.LogWarning($"Options '{obsolete}' is obsolete and ignored...");
			}
			return this;
		}

		private int GetPort(EndPoint nodeEndpoint)
		{
			if (nodeEndpoint is IPEndPoint endPoint)
				return endPoint.Port;
			else if (nodeEndpoint is DnsEndPoint dnsEndPoint)
				return dnsEndPoint.Port;
			throw new NotSupportedException();
		}

		public bool Supports(NBXplorerNetwork network)
		{
			return ChainConfigurations.Any(c => network.CryptoCode == c.CryptoCode);
		}

		public bool NoAuthentication
		{
			get;
			set;
		}
		public string InstanceName
		{
			get;
			set;
		}
		public int TrimEvents { get; set; }
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

		public string AzureServiceBusBlockTopic
		{
			get;
			set;
		}

		public string AzureServiceBusTransactionQueue
		{
			get;
			set;
		}
		public string AzureServiceBusTransactionTopic
		{
			get;
			set;
		}

		public string RabbitMqHostName { get; set; }
        public string RabbitMqVirtualHost { get; set; }
        public string RabbitMqUsername { get; set; }
        public string RabbitMqPassword { get; set; }
        public string RabbitMqTransactionExchange { get; set; }
        public string RabbitMqBlockExchange { get; set; }

		public KeyPathTemplate CustomKeyPathTemplate { get; set; }
		public EndPoint SocksEndpoint { get; set; }
		public NetworkCredential SocksCredentials { get; set; }
	}
}
