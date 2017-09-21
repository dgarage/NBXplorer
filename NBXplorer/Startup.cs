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
			services.AddNBXplorer();
			services.ConfigureNBxplorer(Configuration);
			services.AddMvcCore()
				.AddJsonFormatters()
				.AddFormatterMappings();
		}

		public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, IServiceProvider serviceProvider)
		{
			app.UseNBXplorer();
			app.UseMvc();
		}
	}
}
