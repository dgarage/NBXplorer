using Microsoft.AspNetCore.Hosting;
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

namespace NBXplorer
{
	public static class Extensions
	{
		internal static InsertTransaction CreateInsertTransaction(this TransactionMatch match, uint256 blockHash)
		{
			return new InsertTransaction()
			{
				DerivationStrategy = match.DerivationStrategy,
				TrackedTransaction = new TrackedTransaction() { Transaction = match.Transaction, BlockHash = blockHash }
			};
		}
		internal static uint160 GetHash(this DerivationStrategyBase derivation)
		{
			var data = Encoding.UTF8.GetBytes(derivation.ToString());
			return new uint160(Hashes.RIPEMD160(data, data.Length));
		}


		class MVCConfigureOptions : IConfigureOptions<MvcJsonOptions>
		{
			Serializer _Serializer;
			public MVCConfigureOptions(Serializer serializer)
			{
				_Serializer = serializer;
			}
			public void Configure(MvcJsonOptions options)
			{
				_Serializer.ConfigureSerializer(options.SerializerSettings);
			}
		}

		public static IServiceCollection AddNBXplorer(this IServiceCollection services)
		{
			services.AddSingleton<IObjectModelValidator, NoObjectModelValidator>();
			services.Configure<MvcOptions>(mvc =>
			{
				mvc.Filters.Add(new NBXplorerExceptionFilter());
			});

			services.AddSingleton<IConfigureOptions<MvcJsonOptions>, MVCConfigureOptions>();
			services.TryAddSingleton<ConcurrentChain>(o => new ConcurrentChain(o.GetRequiredService<Network>()));
			services.TryAddSingleton<NetworkInformation>(o => o.GetRequiredService<IOptions<ExplorerConfiguration>>().Value.Network);
			services.TryAddSingleton<Network>(o => o.GetRequiredService<IOptions<NetworkInformation>>().Value.Network);

			services.TryAddSingleton<CallbackInvoker>();
			services.TryAddSingleton<Repository>(o =>
			{
				var configuration = o.GetRequiredService<ExplorerConfiguration>();
				var dbPath = Path.Combine(configuration.DataDir, "db");
				var repo = new Repository(configuration.CreateSerializer(), dbPath);
				if(configuration.Rescan)
				{
					Logs.Configuration.LogInformation("Rescanning...");
					repo.SetIndexProgress(null);
				}
				return repo;
			});
			services.TryAddSingleton<Serializer>();
			services.TryAddSingleton<EventAggregator>();
			services.TryAddSingleton<BitcoinDWaiterAccessor>();
			services.AddSingleton<IHostedService, BitcoinDWaiter>();

			services.AddSingleton<ExplorerConfiguration>(o => o.GetRequiredService<IOptions<ExplorerConfiguration>>().Value);

			services.AddSingleton<Network>(o =>
			{
				var c = o.GetRequiredService<ExplorerConfiguration>();
				return c.Network.Network;
			});
			services.TryAddSingleton<RPCClient>(o =>
			{
				var configuration = o.GetRequiredService<ExplorerConfiguration>();
				configuration.RPC.NoTest = true;
				return configuration.RPC.ConfigureRPCClient(configuration.Network);
			});
			services.TryAddSingleton<RPCAuthorization>(o =>
			{
				var configuration = o.GetRequiredService<ExplorerConfiguration>();
				var cookieFile = Path.Combine(configuration.DataDir, ".cookie");
				var cookieStr = "__cookie__:" + new uint256(RandomUtils.GetBytes(32));
				File.WriteAllText(cookieFile, cookieStr);
				RPCAuthorization auth = new RPCAuthorization();
				if(!configuration.NoAuthentication)
				{
					auth.AllowIp.Add(IPAddress.Parse("127.0.0.1"));
					auth.AllowIp.Add(IPAddress.Parse("::1"));
					auth.Authorized.Add(cookieStr);
				}
				return auth;
			});
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

		public static IApplicationBuilder UseNBXplorer(this IApplicationBuilder app)
		{
			app.UseMiddleware<NBXplorerMiddleware>();
			return app;
		}

		internal class NoObjectModelValidator : IObjectModelValidator
		{
			public void Validate(ActionContext actionContext, ValidationStateDictionary validationState, string prefix, object model)
			{

			}
		}
	}
}
