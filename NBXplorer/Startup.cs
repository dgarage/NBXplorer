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

namespace NBXplorer
{
	public class Startup
	{
		public Startup(IHostingEnvironment env)
		{
			var builder = new ConfigurationBuilder()
				.SetBasePath(env.ContentRootPath)
				.AddEnvironmentVariables();
			Configuration = builder.Build();
		}

		public IConfigurationRoot Configuration
		{
			get;
		}

		// This method gets called by the runtime. Use this method to add services to the container.
		public void ConfigureServices(IServiceCollection services)
		{
			services.AddSingleton<IObjectModelValidator, NoObjectModelValidator>();
			services.AddMvcCore()
				.AddJsonFormatters()
				.AddFormatterMappings();			
		}

		internal class NoObjectModelValidator : IObjectModelValidator
		{
			public void Validate(ActionContext actionContext, ValidationStateDictionary validationState, string prefix, object model)
			{

			}
		}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, IServiceProvider serviceProvider)
		{
			var logging = new FilterLoggerSettings();
			logging.Add("Microsoft.AspNetCore.Hosting.Internal.WebHost", LogLevel.Error);
			logging.Add("Microsoft.AspNetCore.Mvc", LogLevel.Error);
			logging.Add("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Error);
			loggerFactory
				.WithFilter(logging)
				.AddConsole();

			app.UseDeveloperExceptionPage();
			app.UseMvc();

			var config = serviceProvider.GetService<ExplorerRuntime>();
			var options = GetMVCOptions(serviceProvider);
			config.CreateSerializer().ConfigureSerializer(options.SerializerSettings);
		}
		private static MvcJsonOptions GetMVCOptions(IServiceProvider serviceProvider)
		{
			return serviceProvider.GetRequiredService<IOptions<MvcJsonOptions>>().Value;
		}
	}
}
