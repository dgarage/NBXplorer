using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBXplorer.Logging;
using Microsoft.Extensions.Hosting;

namespace NBXplorer
{
	public class Startup
	{
		public Startup(IConfiguration conf, Microsoft.Extensions.Hosting.IHostingEnvironment env)
		{
			Configuration = conf;
			_Env = env;
		}
		Microsoft.Extensions.Hosting.IHostingEnvironment _Env;
		public IConfiguration Configuration
		{
			get;
		}

		public void ConfigureServices(IServiceCollection services)
		{
			services.AddNBXplorer();
			services.ConfigureNBxplorer(Configuration);
			services.AddMessageBrokers(Configuration);
			services.AddMvcCore()
				.AddJsonFormatters()
				.AddAuthorization()
				.AddFormatterMappings();
			services.AddAuthentication("Basic")
				.AddNBXplorerAuthentication();
			
		}

		public void Configure(IApplicationBuilder app, IServiceProvider prov, Microsoft.Extensions.Hosting.IHostingEnvironment env, ILoggerFactory loggerFactory, IServiceProvider serviceProvider,
			CookieRepository cookieRepository)
		{
			cookieRepository.Initialize();
			if(env.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
			}

			Logs.Configure(loggerFactory);
			app.UseAuthentication();
			app.UseWebSockets();
			app.UseMvc();
		}
	}
}
