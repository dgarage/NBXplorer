using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc;
using NBitcoin.JsonConverters;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Http.Features;
using NBXplorer.Filters;
using NBXplorer.Logging;
using Microsoft.AspNetCore.Authentication;
using NBXplorer.Authentication;
using NBXplorer.Configuration;
using Newtonsoft.Json.Serialization;
using Microsoft.Extensions.Hosting;

namespace NBXplorer
{
	public class Startup
	{
		public Startup(IConfiguration conf)
		{
			Configuration = conf;
		}
		public IConfiguration Configuration
		{
			get;
		}

		public void ConfigureServices(IServiceCollection services)
		{
			services.AddHttpClient();
			services.AddHttpClient(nameof(IRPCClients), httpClient =>
			{
				httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
			});
			services.AddNBXplorer(Configuration);
			services.ConfigureNBxplorer(Configuration);
			var builder = services.AddMvcCore();
			services.AddHealthChecks().AddCheck<HealthChecks.NodesHealthCheck>("NodesHealthCheck");
			builder.AddNewtonsoftJson(options =>
			{
				options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
				new Serializer(null).ConfigureSerializer(options.SerializerSettings);
			});
			builder.AddMvcOptions(o => o.InputFormatters.Add(new NoContentTypeInputFormatter()))
			.AddAuthorization()
			.AddFormatterMappings();
			services.AddAuthentication("Basic")
				.AddNBXplorerAuthentication();
		}

		public void Configure(IApplicationBuilder app, IServiceProvider prov,
			ExplorerConfiguration explorerConfiguration,
			IWebHostEnvironment env,
			ILoggerFactory loggerFactory, IServiceProvider serviceProvider,
			CookieRepository cookieRepository)
		{
			cookieRepository.Initialize();
			if(env.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
			}
			Logs.Configure(loggerFactory);
			if (!string.IsNullOrEmpty(explorerConfiguration.InstanceName))
			{
				app.Use(async (httpContext, next) =>
				{
					httpContext.Response.Headers.Add("instance-name", explorerConfiguration.InstanceName);
					await next();
				});
			}
			app.UseRouting();
			app.UseAuthentication();
			app.UseAuthorization();
			app.UseWebSockets();
			//app.UseMiddleware<LogAllRequestsMiddleware>();
			app.UseEndpoints(endpoints =>
			{
				endpoints.MapHealthChecks("health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions()
				{
					ResponseWriter = HealthChecks.HealthCheckWriters.WriteJSON
				});
				endpoints.MapControllers();
			});
		}
	}
}
