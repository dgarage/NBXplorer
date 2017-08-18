using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBXplorer.Configuration;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc;
using NBXplorer.Filters;

namespace NBXplorer
{
	public static class Extensions
	{
		public static IWebHostBuilder UseNBXplorer(this IWebHostBuilder builder, ExplorerConfiguration config)
		{
			if(builder == null)
				throw new ArgumentNullException(nameof(builder));
			if(config == null)
				throw new ArgumentNullException(nameof(config));

			return 
				builder
				.ConfigureServices(services =>
				 {
					 services.AddSingleton(config);
					 services.AddSingleton<ExplorerRuntime>(o =>
					 {
						 var c = o.GetRequiredService<ExplorerConfiguration>();
						 return c.CreateRuntime();
					 });
					 services.AddSingleton<Network>(o =>
					 {
						 var c = o.GetRequiredService<ExplorerConfiguration>();
						 return c.Network;
					 });
					 services.AddSingleton<RPCAuthorization>(o =>
					 {
						 var c = o.GetRequiredService<ExplorerRuntime>();
						 return c.Authorizations;
					 });
				 })
				 .UseStartup<Startup>();
		}
	}
}
