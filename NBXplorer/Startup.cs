using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBXplorer.Logging;
using NBXplorer.Configuration;
using Newtonsoft.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;

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
			services.AddHttpClient(nameof(RPCClientProvider), httpClient =>
			{
				httpClient.Timeout = TimeSpan.FromMinutes(10.0);
			});
			services.AddNBXplorer(Configuration);
			services.ConfigureNBxplorer(Configuration);
			var builder = services.AddMvcCore().AddControllersAsServices();
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
					httpContext.Response.Headers.Append("instance-name", explorerConfiguration.InstanceName);
					await next();
				});
			}
			app.UseRouting();
			app.UseAuthentication();
			app.UseAuthorization();
			app.UseWebSockets();
			app.UseCors();
			app.UseStaticFiles();

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
