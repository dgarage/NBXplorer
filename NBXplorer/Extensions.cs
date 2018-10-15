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
using NBXplorer.DB;

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
				delegate
				{
					tcs.TrySetResult(true);
				}, null, TimeSpan.FromMinutes(1.0), true);
			var t = tcs.Task;
			t.ContinueWith(_ => rwh.Unregister(null));
			return t;
		}
		internal static uint160 GetHash(this DerivationStrategyBase derivation)
		{
			var data = Encoding.UTF8.GetBytes(derivation.ToString());
			return new uint160(Hashes.RIPEMD160(data, data.Length));
		}

		public static bool IsLockUTXO(this Transaction tx)
		{
			return tx.Inputs.Count >= 1 && tx.Inputs[0].PrevOut.N == uint.MaxValue && tx.Inputs[0].PrevOut.Hash == uint256.One;
		}
		public static void MarkLockUTXO(this Transaction tx)
		{
			tx.Inputs.Insert(0, new TxIn(new OutPoint(uint256.One, uint.MaxValue)));
		}

		public static async Task<IEnumerable<TransactionMatch>> GetMatches(this Repository repository, Transaction tx)
		{
			var matches = new Dictionary<DerivationStrategyBase, TransactionMatch>();
			HashSet<Script> inputScripts = new HashSet<Script>();
			HashSet<Script> outputScripts = new HashSet<Script>();
			HashSet<Script> scripts = new HashSet<Script>();
			foreach(var input in tx.Inputs)
			{
				var signer = input.GetSigner();
				if(signer != null)
				{
					inputScripts.Add(signer.ScriptPubKey);
					scripts.Add(signer.ScriptPubKey);
				}
			}

			foreach(var output in tx.Outputs)
			{
				outputScripts.Add(output.ScriptPubKey);
				scripts.Add(output.ScriptPubKey);
			}

			var keyInformations = await repository.GetKeyInformations(scripts.ToArray());
			foreach(var keyInfoByScripts in keyInformations)
			{
				foreach(var keyInfo in keyInfoByScripts.Value)
				{
					if(!matches.TryGetValue(keyInfo.DerivationStrategy, out TransactionMatch match))
					{
						match = new TransactionMatch();
						matches.Add(keyInfo.DerivationStrategy, match);
						match.DerivationStrategy = keyInfo.DerivationStrategy;
						match.Transaction = tx;
					}

					if(outputScripts.Contains(keyInfo.ScriptPubKey))
						match.Outputs.Add(keyInfo);

					if(inputScripts.Contains(keyInfo.ScriptPubKey))
						match.Inputs.Add(keyInfo);
				}
			}
			return matches.Values;
		}


		class MVCConfigureOptions : IConfigureOptions<MvcJsonOptions>
		{
			public void Configure(MvcJsonOptions options)
			{
				new Serializer(null).ConfigureSerializer(options.SerializerSettings);
			}
		}

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

			services.AddSingleton<IConfigureOptions<MvcJsonOptions>, MVCConfigureOptions>();
			services.TryAddSingleton<ChainProvider>();

			services.TryAddSingleton<CookieRepository>();
			services.TryAddSingleton<RepositoryProvider>();
			services.TryAddSingleton<EventAggregator>();
			services.TryAddSingleton<BitcoinDWaitersAccessor>();
			services.AddSingleton<IHostedService, BitcoinDWaiters>();

			services.TryAddSingleton<NBXplorerContextFactory>(o =>
			{
				var opts = o.GetRequiredService<ExplorerConfiguration>();
				NBXplorerContextFactory dbContext = null;
				Logs.Configuration.LogInformation($"Postgres DB used");
				dbContext = new NBXplorerContextFactory(opts.PostgresConnectionString, o.GetRequiredService<Microsoft.AspNetCore.Hosting.IApplicationLifetime>());
				return dbContext;
			});

			services.AddSingleton<ExplorerConfiguration>(o => o.GetRequiredService<IOptions<ExplorerConfiguration>>().Value);

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
