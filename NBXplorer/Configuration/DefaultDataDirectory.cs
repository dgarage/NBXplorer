using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.IO;
using NBXplorer.Logging;

namespace NBXplorer.Configuration
{
	public class DefaultDataDirectory
	{
		public static string GetDefaultDirectory(string appName, Network network)
		{
			string directory = null;
			var home = Environment.GetEnvironmentVariable("HOME");
			if(!string.IsNullOrEmpty(home))
			{
				directory = home;
				directory = Path.Combine(directory, "." + appName.ToLowerInvariant());
			}
			else
			{
				var localAppData = Environment.GetEnvironmentVariable("APPDATA");
				if(!string.IsNullOrEmpty(localAppData))
				{
					directory = localAppData;
					directory = Path.Combine(directory, appName);
				}
				else
				{
					throw new DirectoryNotFoundException("Could not find suitable datadir");
				}
			}
			if(!Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}
			directory = Path.Combine(directory, network.Name);
			if(!Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}
			return directory;
		}
	}
}
