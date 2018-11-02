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

namespace NBXplorer
{
	public class Startup
	{
		public Startup(IConfiguration conf, IHostingEnvironment env)
		{
			Configuration = conf;
			_Env = env;
		}
		IHostingEnvironment _Env;
		public IConfiguration Configuration
		{
			get;
		}

		public void ConfigureServices(IServiceCollection services)
		{
			services.AddNBXplorer();
			services.ConfigureNBxplorer(Configuration);
			services.AddMvcCore()
				.AddJsonFormatters()
				.AddAuthorization()
				.AddFormatterMappings();
			services.AddAuthentication("Basic")
				.AddNBXplorerAuthentication();
		}

		public void Configure(IApplicationBuilder app, IServiceProvider prov, IHostingEnvironment env, ILoggerFactory loggerFactory, IServiceProvider serviceProvider,
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
			//app.UseMiddleware<LogAllRequestsMiddleware>();
			app.UseMvc();
		}
	}
}
