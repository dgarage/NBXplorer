using NBitcoin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NBXplorer.Client
{
    public static class Utils
    {
		public static string GetDefaultCookieFilePath(Network network)
		{
			string directory = null;
			var home = Environment.GetEnvironmentVariable("HOME");
			var appName = "NBXplorer";
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
			directory = Path.Combine(directory, network.Name);
			return Path.Combine(directory, ".cookie");
		}
	}
}
