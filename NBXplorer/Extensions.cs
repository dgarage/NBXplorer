using Microsoft.AspNetCore.Hosting;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NBitcoin;
using NBXplorer.Configuration;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc;
using NBXplorer.Filters;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using NBXplorer.DerivationStrategy;
using NBitcoin.Crypto;
using NBXplorer.Models;
using System.IO;
using NBXplorer.Logging;
using System.Net;
using NBitcoin.RPC;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Authentication;
using NBXplorer.Authentication;
using NBitcoin.DataEncoders;
using System.Text.RegularExpressions;
using NBXplorer.MessageBrokers;
using NBitcoin.Protocol;
using NBXplorer.HostedServices;
using NBXplorer.Controllers;
#if SUPPORT_DBTRIE
using NBXplorer.Backends.DBTrie;
#endif
using NBXplorer.Backends.Postgres;
using NBXplorer.Backends;
using NBitcoin.Altcoins.Elements;

namespace NBXplorer
{
	public static class Extensions
	{
		public static bool IsUnknown(this IMoney money)
		{
			return money is AssetMoney am && am == NBXplorerNetwork.UnknownAssetMoney;
		}
		internal static void AddRange<T>(this HashSet<T> hashset, IEnumerable<T> elements)
		{
			foreach (var el in elements)
				hashset.Add(el);
		}
		internal static uint160 GetHash(this DerivationStrategyBase derivation)
		{
			var data = Encoding.UTF8.GetBytes(derivation.ToString());
			return new uint160(Hashes.RIPEMD160(data, data.Length));
		}
		internal static uint160 GetHash(this TrackedSource trackedSource)
		{
			if (trackedSource is DerivationSchemeTrackedSource t)
				return t.DerivationStrategy.GetHash();
			var data = Encoding.UTF8.GetBytes(trackedSource.ToString());
			return new uint160(Hashes.RIPEMD160(data, data.Length));
		}

		public static T As<T>(this IActionResult actionResult)
		{
			if (actionResult is JsonResult jsonResult && jsonResult.Value is T v)
				return v;
			return default;
		}

		public static async Task<ElementsTransaction> UnblindTransaction(this RPCClient rpc, TrackedTransaction tx, IEnumerable<KeyPathInformation> keyInfos)
		{
			if (tx.TrackedSource is DerivationSchemeTrackedSource ts &&
				!ts.DerivationStrategy.Unblinded() &&
				tx.Transaction is ElementsTransaction elementsTransaction)
			{
				var keys = keyInfos
					.Select(kv => (KeyPath: kv.KeyPath,
								   Address: kv.Address as BitcoinBlindedAddress,
								   BlindingKey: NBXplorerNetworkProvider.LiquidNBXplorerNetwork.GenerateBlindingKey(ts.DerivationStrategy, kv.KeyPath, kv.ScriptPubKey, rpc.Network)))
					.Where(o => o.Address != null)
					.Select(o => new UnblindTransactionBlindingAddressKey()
					{
						Address = o.Address,
						BlindingKey = o.BlindingKey
					}).ToList();
				if (keys.Count != 0)
				{
					return await rpc.UnblindTransaction(keys, elementsTransaction, rpc.Network);
				}
			}
			return null;
		}
		public static async Task<DateTimeOffset?> GetBlockTimeAsync(this RPCClient client, uint256 blockId, bool throwIfNotFound = true)
		{
			var response = await client.SendCommandAsync(new RPCRequest("getblockheader", new object[] { blockId }) { ThrowIfRPCError = throwIfNotFound }).ConfigureAwait(false);
			if(throwIfNotFound)
				response.ThrowIfError();
			if(response.Error != null && response.Error.Code == RPCErrorCode.RPC_INVALID_ADDRESS_OR_KEY)
				return null;
			if(response.Result["time"] != null)
			{
				return NBitcoin.Utils.UnixTimeToDateTime((uint)response.Result["time"]);
			}
			return null;
		}

		internal static KeyPathInformation AddAddress(this KeyPathInformation keyPathInformation, Network network)
		{
			if(keyPathInformation.Address == null)
			{
				keyPathInformation.Address = keyPathInformation.ScriptPubKey.GetDestinationAddress(network);
			}
			return keyPathInformation;
		}
#if NETCOREAPP21
		class MVCConfigureOptions : IConfigureOptions<MvcJsonOptions>
		{
			public void Configure(MvcJsonOptions options)
			{
				new Serializer(null).ConfigureSerializer(options.SerializerSettings);
			}
		}
#endif

		public class ConfigureCookieFileBasedConfiguration : IConfigureNamedOptions<BasicAuthenticationOptions>
		{
			CookieRepository _CookieRepo;
			public ConfigureCookieFileBasedConfiguration(CookieRepository cookieRepo)
			{
				_CookieRepo = cookieRepo;
			}

			public void Configure(string name, BasicAuthenticationOptions options)
			{
				if(name == "Basic")
				{
					var creds = _CookieRepo.GetCredentials();
					if(creds != null)
					{
						options.Username = creds.UserName;
						options.Password = creds.Password;
					}
				}
			}

			public void Configure(BasicAuthenticationOptions options)
			{
				Configure(null, options);
			}
		}

		public static AuthenticationBuilder AddNBXplorerAuthentication(this AuthenticationBuilder builder)
		{
			builder.Services.AddSingleton<IConfigureOptions<BasicAuthenticationOptions>, ConfigureCookieFileBasedConfiguration>();
			return builder.AddScheme<Authentication.BasicAuthenticationOptions, Authentication.BasicAuthenticationHandler>("Basic", o =>
			{

			});
		}

		public static IServiceCollection AddNBXplorer(this IServiceCollection services, IConfiguration configuration)
		{
			services.AddSingleton<IObjectModelValidator, NoObjectModelValidator>();
			services.Configure<MvcOptions>(mvc =>
			{
				mvc.Filters.Add(new NBXplorerExceptionFilter());
			});

#if NETCOREAPP21
			services.AddSingleton<IConfigureOptions<MvcJsonOptions>, MVCConfigureOptions>();
			services.AddSingleton<MvcNewtonsoftJsonOptions>();
#else
			services.AddSingleton<MvcNewtonsoftJsonOptions>(o =>  o.GetRequiredService<IOptions<MvcNewtonsoftJsonOptions>>().Value);
#endif

			services.TryAddSingleton<CookieRepository>();
			services.TryAddSingleton<Broadcaster>();

			// MainController wants to resolve this, even if unused by postgres backend
			services.TryAddSingleton<RebroadcasterHostedService>();
			if (configuration.IsPostgres())
			{
				services.AddHostedService<HostedServices.DatabaseSetupHostedService>();
				services.AddSingleton<IHostedService, IRepositoryProvider>(o => o.GetRequiredService<IRepositoryProvider>());
				services.TryAddSingleton<IRepositoryProvider, PostgresRepositoryProvider>();
				services.AddSingleton<DbConnectionFactory>();
#if SUPPORT_DBTRIE
				if (configuration.GetOrDefault("AUTOMIGRATE", false))
				{
					services.AddHostedService<HostedServices.DBTrieToPostgresMigratorHostedService>();
					services.TryAddSingleton<ChainProvider>();
					services.TryAddSingleton<RepositoryProvider>();
				}
#endif
				services.TryAddTransient<IUTXOService, PostgresMainController>();
				services.TryAddSingleton<PostgresIndexers>();
				services.TryAddSingleton<IIndexers>(o => o.GetRequiredService<PostgresIndexers>());
				services.AddSingleton<IHostedService, PostgresIndexers>(o => o.GetRequiredService<PostgresIndexers>());

				services.AddSingleton<CheckMempoolTransactionsPeriodicTask>();
				services.AddSingleton<RefreshWalletHistoryPeriodicTask>();
				services.AddTransient<ScheduledTask>(o => new ScheduledTask(typeof(RefreshWalletHistoryPeriodicTask), TimeSpan.FromMinutes(30.0)));
				services.AddTransient<ScheduledTask>(o => new ScheduledTask(typeof(CheckMempoolTransactionsPeriodicTask), TimeSpan.FromMinutes(5.0)));
				services.AddHostedService<PeriodicTaskLauncherHostedService>();
			}
#if SUPPORT_DBTRIE
			else
			{
				services.TryAddTransient<IUTXOService, MainController>();
				services.TryAddSingleton<ChainProvider>();
				services.AddSingleton<IHostedService, IRepositoryProvider>(o => o.GetRequiredService<IRepositoryProvider>());
				services.TryAddSingleton<IRepositoryProvider, RepositoryProvider>();
				services.TryAddSingleton<BitcoinDWaiters>();
				services.TryAddSingleton<IIndexers>(o => o.GetRequiredService<BitcoinDWaiters>());
				services.AddSingleton<IHostedService, BitcoinDWaiters>(o => o.GetRequiredService<BitcoinDWaiters>());
				services.AddSingleton<IHostedService, RebroadcasterHostedService>(o => o.GetRequiredService<RebroadcasterHostedService>());
			}
#endif

			services.TryAddSingleton<EventAggregator>();
			services.TryAddSingleton<AddressPoolService>();
			services.AddSingleton<IHostedService, AddressPoolService>(o => o.GetRequiredService<AddressPoolService>());
			services.TryAddSingleton<IRPCClients, RPCClientProvider>();
			services.AddHostedService<RPCReadyFileHostedService>();
			services.AddSingleton<IHostedService, ScanUTXOSetService>();
			services.TryAddSingleton<ScanUTXOSetServiceAccessor>();
			services.AddSingleton<IHostedService, BrokerHostedService>();

			services.AddSingleton<Analytics.FingerprintHostedService>();
			services.AddSingleton<IHostedService, Analytics.FingerprintHostedService>(o => o.GetRequiredService<Analytics.FingerprintHostedService>());

			services.AddSingleton<ExplorerConfiguration>(o => o.GetRequiredService<IOptions<ExplorerConfiguration>>().Value);

			services.AddSingleton<KeyPathTemplates>(o =>
			{
				var conf = o.GetRequiredService<IOptions<ExplorerConfiguration>>().Value;
				return new KeyPathTemplates(conf.CustomKeyPathTemplate);
			});

			services.AddSingleton<NBXplorerNetworkProvider>(o =>
			{
				var c = o.GetRequiredService<ExplorerConfiguration>();
				return c.NetworkProvider;
			});
			services.TryAddSingleton<IRPCClients>();
			return services;
		}

		public static IServiceCollection ConfigureNBxplorer(this IServiceCollection services, IConfiguration conf)
		{
			services.Configure<ExplorerConfiguration>(o =>
			{
				o.LoadArgs(conf);
			});
			return services;
		}

		internal class NoObjectModelValidator : IObjectModelValidator
		{
			public void Validate(ActionContext actionContext, ValidationStateDictionary validationState, string prefix, object model)
			{

			}
		}
	}
}
