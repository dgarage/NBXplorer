using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using NBXplorer.Configuration;
using NBXplorer.Logging;
using NBitcoin.Protocol;
using System.Collections;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore;
using NBitcoin;
using System.Text;
using Microsoft.Extensions.CommandLineUtils;
using System.Net;

namespace NBXplorer
{
	public class Program
	{
		public static void Main(string[] args)
		{
			var processor = new ConsoleLoggerProcessor();
			Logs.Configure(new FuncLoggerFactory(i => new CustomerConsoleLogger(i, (a, b) => true, false, processor)));
			IWebHost host = null;
			try
			{
				ConfigurationBuilder builder = new ConfigurationBuilder();
				host = new WebHostBuilder()
					.UseKestrel()
					.UseIISIntegration()
					.UseConfiguration(DefaultConfiguration.CreateConfiguration(args))
					.UseStartup<Startup>()
					.Build();
				host.Run();
			}
			catch(ConfigException ex)
			{
				if(!string.IsNullOrEmpty(ex.Message))
					Logs.Configuration.LogError(ex.Message);
			}
			catch(Exception exception)
			{
				Logs.Explorer.LogError("Exception thrown while running the server");
				Logs.Explorer.LogError(exception.ToString());
			}
			finally
			{
				if(host != null)
					host.Dispose();
			}
		}

		
	}
}
