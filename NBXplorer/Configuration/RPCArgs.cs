using NBitcoin;
using NBitcoin.RPC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NBXplorer.Logging;
using System.Net;
using Microsoft.Extensions.Configuration;
using System.Threading;

namespace NBXplorer.Configuration
{
	public class RPCArgs
	{
		public Uri Url
		{
			get; set;
		}
		public string User
		{
			get; set;
		}
		public string Password
		{
			get; set;
		}
		public string CookieFile
		{
			get; set;
		}
		public bool NoTest
		{
			get;
			set;
		}
		public string AuthenticationString
		{
			get;
			set;
		}

		public RPCClient ConfigureRPCClient(NetworkInformation networkInformation)
		{
			var network = networkInformation.Network;
			RPCClient rpcClient = null;
			var url = Url;
			var usr = User;
			var pass = Password;
			if(url != null && usr != null && pass != null)
				rpcClient = new RPCClient(new System.Net.NetworkCredential(usr, pass), url, network);
			if(rpcClient == null)
			{
				if(url != null && CookieFile != null)
				{
					try
					{
						rpcClient = new RPCClient(new RPCCredentialString() { CookieFile = CookieFile }, url, network);
					}
					catch(IOException)
					{
						Logs.Configuration.LogWarning("RPC Cookie file not found at " + CookieFile);
					}
				}

				if(AuthenticationString != null)
				{
					rpcClient = new RPCClient(RPCCredentialString.Parse(AuthenticationString), url, network);
				}

				if(rpcClient == null)
				{
					try
					{
						rpcClient = new RPCClient(null as NetworkCredential, url, network);
					}
					catch { }
					if(rpcClient == null)
					{
						Logs.Configuration.LogError("RPC connection settings not configured");
						throw new ConfigException();
					}
				}
			}
			if(NoTest)
				return rpcClient;

			TestRPCAsync(networkInformation, rpcClient).GetAwaiter().GetResult();
			return rpcClient;
		}

		public static async Task TestRPCAsync(NetworkInformation networkInfo, RPCClient rpcClient)
		{
			var network = networkInfo.Network;
			Logs.Configuration.LogInformation("Testing RPC connection to " + rpcClient.Address.AbsoluteUri);
			try
			{
				var address = new Key().PubKey.GetAddress(network);
				int time = 0;
				while(true)
				{
					time++;
					try
					{

						var isValid = ((JObject)(await rpcClient.SendCommandAsync("validateaddress", address.ToString())).Result)["isvalid"].Value<bool>();
						if(!isValid)
						{
							Logs.Configuration.LogError("The RPC Server is on a different blockchain than the one configured for tumbling");
							throw new ConfigException();
						}
						break;
					}
					catch(RPCException ex) when(IsTransient(ex))
					{
						Logs.Configuration.LogInformation($"Transient error '{ex.Message}', retrying soon...");
						Thread.Sleep(Math.Min(1000 * time, 10000));
					}
				}
			}
			catch(ConfigException)
			{
				throw;
			}
			catch(RPCException ex)
			{
				Logs.Configuration.LogError("Invalid response from RPC server " + ex.Message);
				throw new ConfigException();
			}
			catch(Exception ex)
			{
				Logs.Configuration.LogError("Error connecting to RPC server " + ex.Message);
				throw new ConfigException();
			}
			Logs.Configuration.LogInformation("RPC connection successfull");

			var getInfo = await rpcClient.SendCommandAsync(RPCOperations.getinfo);
			var version = ((JObject)getInfo.Result)["version"].Value<int>();
			if(version < networkInfo.MinRPCVersion)
			{
				Logs.Configuration.LogError($"The minimum Bitcoin version required is {networkInfo.MinRPCVersion} (detected: {version})");
				throw new ConfigException();
			}
			Logs.Configuration.LogInformation($"Bitcoin version detected: {version}");
		}

		private static bool IsTransient(RPCException ex)
		{
			return ex.Message.Contains("Loading wallet...") || 
				   ex.Message.Contains("Loading block index...") ||
				   ex.Message.Contains("Loading P2P addresses...");
		}

		public static void CheckNetwork(Network network, RPCClient rpcClient)
		{
			if(network.GenesisHash != null && rpcClient.GetBlockHash(0) != network.GenesisHash)
			{
				Logs.Configuration.LogError("The RPC server is not using the chain " + network.Name);
				throw new ConfigException();
			}
		}

		public static RPCArgs Parse(IConfiguration confArgs, Network network, string prefix = null)
		{
			prefix = prefix ?? "";
			if(prefix != "")
			{
				if(!prefix.EndsWith("."))
					prefix += ".";
			}
			try
			{
				var url = confArgs.GetOrDefault<string>(prefix + "rpc.url", network == null ? null : "http://localhost:" + network.RPCPort + "/");
				return new RPCArgs()
				{
					User = confArgs.GetOrDefault<string>(prefix + "rpc.user", null),
					Password = confArgs.GetOrDefault<string>(prefix + "rpc.password", null),
					CookieFile = confArgs.GetOrDefault<string>(prefix + "rpc.cookiefile", null),
					AuthenticationString = confArgs.GetOrDefault<string>(prefix + "rpc.auth", null),
					NoTest = confArgs.GetOrDefault<bool>(prefix + "rpc.notest", false),
					Url = url == null ? null : new Uri(url)
				};
			}
			catch(FormatException)
			{
				throw new ConfigException("rpc.url is not an url");
			}
		}
	}
}
