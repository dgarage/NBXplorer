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

		public NetworkInformation Network
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

		public ExplorerConfiguration LoadArgs(IConfiguration config)
		{
			Network = DefaultConfiguration.GetNetwork(config);
			if(Network == null)
				throw new ConfigException("Invalid network");

			DataDir = config.GetOrDefault<string>("datadir", Network.DefaultDataDirectory);

			Logs.Configuration.LogInformation("Network: " + Network.Network);

			Rescan = config.GetOrDefault<bool>("rescan", false);

			RPC = RPCArgs.Parse(config, Network.Network);
			NodeEndpoint = DefaultConfiguration.ConvertToEndpoint(config.GetOrDefault<string>("node.endpoint", "127.0.0.1"), Network.Network.DefaultPort);
			CacheChain = config.GetOrDefault<bool>("cachechain", true);
			StartHeight = config.GetOrDefault<int>("startheight", -1);
			NoAuthentication = config.GetOrDefault<bool>("noauth", false);
			return this;
		}

		public Serializer CreateSerializer()
		{
			return new Serializer(Network.Network);
		}

		public int StartHeight
		{
			get; set;
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
		public bool NoAuthentication
		{
			get;
			set;
		}

		public ExplorerRuntime CreateRuntime()
		{
			return new ExplorerRuntime(this);
		}
	}
}
