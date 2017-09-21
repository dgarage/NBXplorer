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

		public ExplorerConfiguration LoadArgs(IConfiguration config)
		{
			Network = NetworkInformation.GetNetworkByName(config.GetOrDefault<string>("network", NBitcoin.Network.Main.Name));
			if(Network == null)
				throw new ConfigurationException("Invalid network");

			DataDir = config.GetOrDefault<string>("datadir", Network.DefaultDataDirectory);

			Logs.Configuration.LogInformation("Network: " + Network.Network);
			Logs.Configuration.LogInformation("Data directory set to " + Path.GetFullPath(DataDir));

			Rescan = config.GetOrDefault<bool>("rescan", false);
			var defaultPort = config.GetOrDefault<int>("port", Network.DefaultExplorerPort);
			Listen = config
						.GetAll("bind")
						.Select(p => ConvertToEndpoint(p, defaultPort))
						.ToList();
			if(Listen.Count == 0)
			{
				Listen.Add(new IPEndPoint(IPAddress.Parse("127.0.0.1"), defaultPort));
			}

			RPC = RPCArgs.Parse(config, Network.Network);
			NodeEndpoint = ConvertToEndpoint(config.GetOrDefault<string>("node.endpoint", "127.0.0.1"), Network.Network.DefaultPort);
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
		public bool NoAuthentication
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

			IPAddress ip = null;

			if(!IPAddress.TryParse(hostOut, out ip))
			{
				ip = Dns.GetHostEntry(hostOut).AddressList.FirstOrDefault();
				if(ip == null)
					throw new FormatException("Invalid IP Endpoint");
			}

			return new IPEndPoint(ip, portOut);
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
