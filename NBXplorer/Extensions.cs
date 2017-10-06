using Microsoft.AspNetCore.Hosting;
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

		public static IServiceCollection AddNBXplorer(this IServiceCollection services)
		{
			services.AddSingleton<IObjectModelValidator, NoObjectModelValidator>();
			services.Configure<MvcOptions>(mvc =>
			{
				mvc.Filters.Add(new NBXplorerExceptionFilter());
			});

			services.TryAddSingleton<CallbackInvoker>(o => o.GetRequiredService<ExplorerRuntime>().CallbackInvoker);
			services.TryAddSingleton<Repository>(o => o.GetRequiredService<ExplorerRuntime>().Repository);
			services.AddSingleton<ExplorerConfiguration>(o => o.GetRequiredService<IOptions<ExplorerConfiguration>>().Value);
			services.AddSingleton<ExplorerRuntime>(o =>
			{
				var c = o.GetRequiredService<IOptions<ExplorerConfiguration>>();
				return c.Value.CreateRuntime();
			});

			services.AddSingleton<Network>(o =>
			{
				var c = o.GetRequiredService<ExplorerConfiguration>();
				return c.Network.Network;
			});
			services.AddSingleton<RPCAuthorization>(o =>
			{
				var c = o.GetRequiredService<ExplorerRuntime>();
				return c.Authorizations;
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
			var mvcOptions = app.ApplicationServices.GetRequiredService<IOptions<MvcJsonOptions>>().Value;
			var runtime = app.ApplicationServices.GetRequiredService<ExplorerRuntime>();
			runtime.CreateSerializer().ConfigureSerializer(mvcOptions.SerializerSettings);
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
