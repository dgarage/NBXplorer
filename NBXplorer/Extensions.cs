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
using System.Threading.Tasks;
using System.Threading;

namespace NBXplorer
{
	public static class Extensions
	{
		internal static Task WaitOneAsync(this WaitHandle waitHandle)
		{
			if(waitHandle == null)
				throw new ArgumentNullException("waitHandle");

			var tcs = new TaskCompletionSource<bool>();
			var rwh = ThreadPool.RegisterWaitForSingleObject(waitHandle,
				delegate {
					tcs.TrySetResult(true);
				}, null, TimeSpan.FromMinutes(1.0), true);
			var t = tcs.Task;
			t.ContinueWith(_ => rwh.Unregister(null));
			return t;
		}
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
			public void Configure(MvcJsonOptions options)
			{
				new Serializer(null).ConfigureSerializer(options.SerializerSettings);
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
			services.TryAddSingleton<ChainProvider>();

			services.TryAddSingleton<RepositoryProvider>();
			services.TryAddSingleton<EventAggregator>();
			services.TryAddSingleton<BitcoinDWaitersAccessor>();
			services.AddSingleton<IHostedService, BitcoinDWaiters>();

			services.AddSingleton<ExplorerConfiguration>(o => o.GetRequiredService<IOptions<ExplorerConfiguration>>().Value);

			services.AddSingleton<NBXplorerNetworkProvider>(o =>
			{
				var c = o.GetRequiredService<ExplorerConfiguration>();
				return c.NetworkProvider;
			});
			services.TryAddSingleton<RPCClientProvider>();
			services.TryAddSingleton<RPCAuthorization>(o =>
			{
				var configuration = o.GetRequiredService<ExplorerConfiguration>();
				var cookieFile = Path.Combine(configuration.DataDir, ".cookie");
				var cookieStr = "__cookie__:" + new uint256(RandomUtils.GetBytes(32));
				File.WriteAllText(cookieFile, cookieStr);
				RPCAuthorization auth = new RPCAuthorization();
				if(!configuration.NoAuthentication)
				{
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
