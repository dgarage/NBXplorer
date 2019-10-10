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
using NBitcoin.Altcoins.Elements;
using NBXplorer.MessageBrokers;
using NBitcoin.Protocol;

namespace NBXplorer
{
	public static class Extensions
	{
		internal static bool AsBoolean(this string value)
		{
			if (value is string str && bool.TryParse(str, out var v))
				return v;
			return false;
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
		public static async Task<DateTimeOffset?> GetBlockTimeAsync(this RPCClient client, uint256 blockId, bool throwIfNotFound = true)
		{
			var response = await client.SendCommandAsync(new RPCRequest("getblockheader", new object[] { blockId }), throwIfNotFound).ConfigureAwait(false);
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

		public static IServiceCollection AddNBXplorer(this IServiceCollection services)
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
			services.TryAddSingleton<ChainProvider>();

			services.TryAddSingleton<CookieRepository>();
			services.TryAddSingleton<RepositoryProvider>();
			services.TryAddSingleton<EventAggregator>();
			services.TryAddSingleton<AddressPoolServiceAccessor>();
			services.AddSingleton<IHostedService, AddressPoolService>();
			services.TryAddSingleton<BitcoinDWaiters>();
			services.TryAddSingleton<RebroadcasterHostedService>();
			services.AddSingleton<IHostedService, ScanUTXOSetService>();
			services.TryAddSingleton<ScanUTXOSetServiceAccessor>();
			services.AddSingleton<IHostedService, BitcoinDWaiters>(o => o.GetRequiredService<BitcoinDWaiters>());
			services.AddSingleton<IHostedService, RebroadcasterHostedService>(o => o.GetRequiredService<RebroadcasterHostedService>());
			services.AddSingleton<IHostedService, BrokerHostedService>();

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
			services.TryAddSingleton<RPCClientProvider>();
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
