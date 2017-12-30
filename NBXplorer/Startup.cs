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
using Microsoft.ApplicationInsights.AspNetCore.Extensions;

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
				.AddFormatterMappings();
			services.Configure<IOptions<ApplicationInsightsServiceOptions>>(o =>
			{
				o.Value.DeveloperMode = _Env.IsDevelopment();
			});
		}

		public void Configure(IApplicationBuilder app, IServiceProvider prov, IHostingEnvironment env, ILoggerFactory loggerFactory, IServiceProvider serviceProvider)
		{
			if(env.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
			}
			loggerFactory.AddApplicationInsights(prov, LogLevel.Information);
			app.UseNBXplorer();
			app.UseWebSockets();
			app.UseMvc();
		}
	}
}
