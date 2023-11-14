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
		public bool HasTxIndex { get; set; }
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
#if SUPPORT_DBTRIE
		public bool IsPostgres { get; set; }
		public bool IsDbTrie { get; set; }
		public bool NoMigrateEvents { get; private set; }
		public bool NoMigrateRawTxs { get; private set; }
		public int DBCache { get; set; }
#else
		public bool IsPostgres => true;
#endif
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
					
					chainConfiguration.HasTxIndex = config.GetOrDefault<bool>($"{network.CryptoCode}.hastxindex", false);
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
#if SUPPORT_DBTRIE
			DBCache = config.GetOrDefault<int>("dbcache", 50);
			if (DBCache > 0)
				Logs.Configuration.LogInformation($"DBCache: {DBCache} MB");
#endif
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
#if SUPPORT_DBTRIE
			CacheChain = config.GetOrDefault<bool>("cachechain", true);
#endif
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
#if SUPPORT_DBTRIE
			IsPostgres = config.IsPostgres();
			IsDbTrie = config.GetOrDefault<bool>("dbtrie", false); ;
			NoMigrateEvents = config.GetOrDefault<bool>("nomigrateevts", false);
			NoMigrateRawTxs = config.GetOrDefault<bool>("nomigraterawtxs", false);
			if (!IsPostgres && !IsDbTrie)
			{
				throw new ConfigException("You need to select your backend implementation. There is two choices, PostgresSQL and DBTrie." + Environment.NewLine +
					"  * To use postgres, please use --postgres \"...\" (or NBXPLORER_POSTGRES=\"...\") with a postgres connection string (see https://www.connectionstrings.com/postgresql/)" + Environment.NewLine +
					"  * To use DBTrie, use --dbtrie (or NBXPLORER_DBTRIE=1). This backend is deprecated, only use if you haven't yet migrated. For more information about how to migrate, see https://github.com/dgarage/NBXplorer/tree/master/docs/Postgres-Migration.md");
			}
			if (IsDbTrie)
			{
				Logs.Configuration.LogWarning("Warning: A DBTrie backend has been selected, but this backend is deprecated, only use if you haven't yet migrated to postgres. For more information about how to migrate, see https://github.com/dgarage/NBXplorer/tree/master/docs/Postgres-Migration.md");
			}
			if (IsDbTrie && IsPostgres)
			{
				throw new ConfigException("You need to select your backend implementation. But --dbtrie and --postgres are both specified.");
			}
#endif
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

#if SUPPORT_DBTRIE
		public bool CacheChain
		{
			get;
			set;
		}
#endif
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
