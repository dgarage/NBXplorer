using Microsoft.Extensions.Configuration;
using NBXplorer.Configuration;

namespace NBXplorer
{
	public static class ConfigurationExtensions
	{
		public static string GetRequired(this IConfiguration conf, string key)
		{
			if (conf[key] is string s)
				return s;
			throw new ConfigException($"{key} is required");
		}
	}
}
