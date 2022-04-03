﻿using NBitcoin;
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

		public RPCClient ConfigureRPCClient(NBXplorerNetwork networkInformation)
		{
			var network = networkInformation.NBitcoinNetwork;
			RPCClient rpcClient = null;
			var url = Url;
			var usr = User;
			var pass = Password;
			if(url != null && usr != null && pass != null)
				rpcClient = new RPCClient(new System.Net.NetworkCredential(usr, pass), url, network);
			if(rpcClient == null)
			{
				if(CookieFile != null)
				{
					try
					{
						rpcClient = new RPCClient(new RPCCredentialString() { CookieFile = CookieFile }, url, network);
					}
					catch(IOException)
					{
						Logs.Configuration.LogWarning($"{networkInformation.CryptoCode}: RPC Cookie file not found at " + (CookieFile ?? RPCClient.GetDefaultCookieFilePath(network)));
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
						Logs.Configuration.LogError($"{networkInformation.CryptoCode}: RPC connection settings not configured");
						throw new ConfigException();
					}
				}
			}
			return rpcClient;
		}

		public static async Task<RPCCapabilities> TestRPCAsync(NBXplorerNetwork networkInfo, RPCClient rpcClient, CancellationToken cancellation, ILogger logger)
		{
			var network = networkInfo.NBitcoinNetwork;
			logger.LogInformation($"Testing RPC connection to " + rpcClient.Address.AbsoluteUri);

			RPCResponse blockchainInfo = null;
			try
			{
				int time = 0;
				retry:
				try
				{
					blockchainInfo = await rpcClient.SendCommandAsync("getblockchaininfo", cancellation);
					blockchainInfo.ThrowIfError();
				}
				catch when (cancellation.IsCancellationRequested)
				{
					throw;
				}
				catch (RPCException ex) when (IsTransient(ex))
				{
					logger.LogInformation($"Transient error '{ex.Message}', retrying soon...");
					time++;
					await Task.Delay(Math.Min(1000 * time, 10000), cancellation);
					goto retry;
				}
			}
			catch when (cancellation.IsCancellationRequested)
			{
				throw;
			}
			catch (ConfigException)
			{
				throw;
			}
			catch(RPCException ex)
			{
				logger.LogError($"Invalid response from RPC server " + ex.Message);
				throw new ConfigException();
			}
			catch(Exception ex)
			{
				logger.LogError($"Error connecting to RPC server " + ex.Message);
				throw new ConfigException();
			}

			logger.LogInformation($"RPC connection successful");

			var capabilities = await rpcClient.ScanRPCCapabilitiesAsync(cancellation);
			if (capabilities.Version < networkInfo.MinRPCVersion)
			{
				logger.LogError($"The minimum node version required is {networkInfo.MinRPCVersion} (detected: {capabilities.Version})");
				throw new ConfigException();
			}
			logger.LogInformation($"Full node version detected: {capabilities.Version}");
			return capabilities;
		}

		private static bool IsTransient(RPCException ex)
		{
			return
				   ex.RPCCode == RPCErrorCode.RPC_IN_WARMUP ||
				   ex.Message.Contains("Loading wallet...") ||
				   ex.Message.Contains("Loading block index...") ||
				   ex.Message.Contains("Loading P2P addresses...") ||
				   ex.Message.Contains("Rewinding blocks...") ||
				   ex.Message.Contains("Verifying blocks...") ||
				   ex.Message.Contains("Loading addresses...");
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
