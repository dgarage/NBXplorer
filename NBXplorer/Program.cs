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

namespace NBXplorer
{
	public class Program
	{
		public static void Main(string[] args)
		{
			Logs.Configure(new FuncLoggerFactory(i => new CustomerConsoleLogger(i, (a, b) => true, false)));
			IWebHost host = null;
			try
			{
				var conf = new ExplorerConfiguration();

				var arguments = new TextFileConfiguration(args);
				arguments = LoadEnvironmentVariables(arguments);
				conf.LoadArgs(arguments);

				host = new WebHostBuilder()
					.UseNBXplorer(conf)
					.UseUrls(conf.GetUrls())
					.UseIISIntegration()
					.UseKestrel()
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

		private static TextFileConfiguration LoadEnvironmentVariables(TextFileConfiguration args)
		{
			var variables = Environment.GetEnvironmentVariables();
			List<string> values = new List<string>();
			foreach(DictionaryEntry variable in variables)
			{
				var key = (string)variable.Key;
				var value = (string)variable.Value;
				if(key.StartsWith("APPSETTING_", StringComparison.Ordinal))
				{
					key = key.Substring("APPSETTING_".Length);
					values.Add("-" + key);
					values.Add(value);
				}
			}

			TextFileConfiguration envConfig = new TextFileConfiguration(values.ToArray());
			args.MergeInto(envConfig, true);
			return envConfig;
		}
	}
}
