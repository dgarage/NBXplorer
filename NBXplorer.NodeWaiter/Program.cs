using CommandLine;
using System.Linq;
//using Microsoft.Extensions.Logging;
using System;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
//using NBitcoin;
using CommandLine.Configuration;
using System.Collections.Generic;
using System.Diagnostics;
using NBitcoin;

namespace NBXplorer.NodeWaiter
{
	class Program
	{
		static async Task<int> Main(string[] args)
		{
			var childProcessArgSeparatorPos = Array.IndexOf(args, "--");
			var waiterArgs = args;
			string[] childProcessArgs = null;
			string childProcess = null;
			if (childProcessArgSeparatorPos != -1)
			{
				waiterArgs = args.Take(childProcessArgSeparatorPos).ToArray();
				if (childProcessArgSeparatorPos != args.Length - 1)
				{
					childProcess = args.Skip(childProcessArgSeparatorPos + 1).First();
					childProcessArgs = args.Skip(childProcessArgSeparatorPos + 2).ToArray();
				}
			}


			var validChains = string.Join(",", new NBXplorerNetworkProvider(NetworkType.Mainnet).GetAll().Select(n => n.CryptoCode.ToLowerInvariant()).ToArray());
			using (CancellationTokenSource stop = new CancellationTokenSource())
			{
				var provider = new CommandLineExConfigurationProvider(waiterArgs, CreateCommandLineApplication);
				provider.Load();

				if(!provider.TryGet("explorerurl", out var unused) || unused == string.Empty)
				{
					Console.WriteLine("explorerurl not configued, skipping waiting");
					return RunChildProcess(childProcess, childProcessArgs);
				}

				if (provider.TryGet("help", out unused))
				{
					return 1;
				}
				if (!provider.TryGet("chains", out var chains))
				{
					chains = "btc";
				}
				if (!provider.TryGet("network", out var network))
				{
					network = "mainnet";
				}

				var networkType = GetNetworkType(network);
				var networkProvider = new NBXplorerNetworkProvider(networkType);

				var supportedChains = chains
									  .Split(',', StringSplitOptions.RemoveEmptyEntries)
									  .Select(t => GetNetwork(provider, networkProvider, t, validChains))
									  .ToList();

				var wait = TimeSpan.FromSeconds(1);
				while (true)
				{
					if (await AreSynchedAndStarted(supportedChains, stop.Token))
					{
						return RunChildProcess(childProcess, childProcessArgs);
					}
					Write($"-----trying again in {(int)wait.TotalSeconds} seconds-----");
					await Task.Delay(wait, stop.Token);
					wait = TimeSpan.FromTicks(2 * wait.Ticks);
					if (wait > TimeSpan.FromMinutes(5))
						wait = TimeSpan.FromMinutes(5);
				}
			}
		}

		private static int RunChildProcess(string process, string[] args)
		{
			if (process == null)
				return 0;
			var childargs = string.Join(" ", args);
			Console.WriteLine("Starting and forwarding signals to: \"" + process + "\" " + childargs);
			var exitForwarder = ExitForwarder.ForwardToChild(Process.Start(process, childargs));
			Console.WriteLine("Implementation: " + exitForwarder.GetType().Name);
			return exitForwarder.WaitForExitAndForward();
		}

		private static async Task<bool> AreSynchedAndStarted(List<ExplorerClient> explorerClients, CancellationToken token)
		{
			var tasks = explorerClients
				.Select(async client =>
				{
					try
					{
						var status = await client.GetStatusAsync(token);

						if (status.IsFullySynched)
						{
							Write($"{client.CryptoCode}: Is fully synched");
							return true;
						}
						else if (status.BitcoinStatus != null && !status.BitcoinStatus.IsSynched)
						{
							Write($"{client.CryptoCode}: Is synching {status.BitcoinStatus.Blocks}/{status.BitcoinStatus.Headers} ({Math.Round(status.BitcoinStatus.VerificationProgress * 100, 2)} %)");
						}
						else if (status.BitcoinStatus != null && status.BitcoinStatus.IsSynched)
						{
							Write($"{client.CryptoCode}: Is fully synched");
							return true;
						}
						else if (status.BitcoinStatus == null)
						{
							Write($"{client.CryptoCode}: Is offline");
						}
						return false;
					}
					catch (Exception ex)
					{
						Write($"{client.CryptoCode}: Error while trying to contact the node {ex.Message}");
						return false;
					}
				})
				.ToArray();
			await Task.WhenAll(tasks);
			return tasks.Select(t => t.Result).All(o => o);
		}

		static object l = new object();
		private static void Write(string v)
		{
			lock (l)
			{
				Console.WriteLine(v);
			}
		}

		private static ExplorerClient GetNetwork(CommandLineExConfigurationProvider config, NBXplorerNetworkProvider networkProvider, string cryptoCode, string validValues)
		{
			var network = networkProvider.GetFromCryptoCode(cryptoCode);
			if (network == null)
				throw new NotSupportedException($"{cryptoCode} in --chains is not supported, valid value: {validValues}");

			Uri uri = null;
			if (!config.TryGet($"explorerurl", out var uriStr))
				uri = network.DefaultSettings.DefaultUrl;
			else
				uri = new Uri(uriStr, UriKind.Absolute);
			return new ExplorerClient(network, uri);
		}

		private static NetworkType GetNetworkType(string network)
		{
			switch (network.ToLowerInvariant())
			{
				case "mainnet":
					return NetworkType.Mainnet;
				case "testnet":
					return NetworkType.Testnet;
				case "regtest":
					return NetworkType.Regtest;
				default:
					throw new NotSupportedException($"{network} in --network is not supported, valid value: mainnet, testnet, regtest");
			}
		}

		private static CommandLineApplication CreateCommandLineApplication()
		{
			var provider = new NBXplorerNetworkProvider(NetworkType.Mainnet);
			var chains = string.Join(",", provider.GetAll().Select(n => n.CryptoCode.ToLowerInvariant()).ToArray());
			CommandLineApplication app = new CommandLineApplication(true)
			{
				FullName = "NBXplorer NodeWaiter\r\nUtility which exit only when the NBXplorer Server is fully up",
				Name = "NBXplorer Waiter"
			};
			app.HelpOption("-? | -h | --help");
			app.Option("-n | --network", $"Set the network among (mainnet,testnet,regtest) (default: mainnet)", CommandOptionType.SingleValue);
			app.Option("--chains", $"Chains to support comma separated (default: btc, available: {chains})", CommandOptionType.SingleValue);
			app.Option($"--explorerurl", "The url to nbxplorer instance", CommandOptionType.SingleValue);
			return app;
		}
	}
}
