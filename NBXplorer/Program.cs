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
using System.Net;
using CommandLine;

namespace NBXplorer
{
	public class Program
	{
		public static void Main(string[] args)
		{
			var processor = new ConsoleLoggerProcessor();
			Logs.Configure(new FuncLoggerFactory(i => new CustomerConsoleLogger(i, (a, b) => true, null, processor)));
			IWebHost host = null;
			try
			{
				var conf = new DefaultConfiguration() { Logger = Logs.Configuration }.CreateConfiguration(args);
				if(conf == null)
					return;

				// Sanity check of the config, this is not strictly needed as it would happen down the line when the host is built
				// However, a bug in .NET Core fixed in 2.1 will prevent the app from stopping if an exception is thrown by the host
				// at startup. We need to remove this line later
				new ExplorerConfiguration().LoadArgs(conf);

				ConfigurationBuilder builder = new ConfigurationBuilder();
				host = new WebHostBuilder()
					.UseKestrel()
					.UseIISIntegration()
					.UseConfiguration(conf)
					.ConfigureLogging(l =>
					{
						l.AddFilter("Microsoft", LogLevel.Error);
						l.AddFilter("NBXplorer.Authentication.BasicAuthenticationHandler", LogLevel.Critical);
						if(conf.GetOrDefault<bool>("verbose", false))
						{
							l.SetMinimumLevel(LogLevel.Debug);
						}
						l.AddProvider(new CustomConsoleLogProvider(processor));
					})
					.UseStartup<Startup>()
					.Build();
				host.Run();
			}
			catch(ConfigException ex)
			{
				if(!string.IsNullOrEmpty(ex.Message))
					Logs.Configuration.LogError(ex.Message);
			}
			catch(CommandParsingException parsing)
			{
				Logs.Explorer.LogError(parsing.HelpText + "\r\n" + parsing.Message);
			}
			finally
			{
				processor.Dispose();
				if(host != null)
					host.Dispose();
			}
		}


	}
}
