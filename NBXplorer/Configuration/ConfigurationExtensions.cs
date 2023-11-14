using Microsoft.Extensions.Configuration;
using System;
using System.Globalization;
using System.Linq;

namespace NBXplorer.Configuration
{
    public static class ConfigurationExtensions
    {
		public static T GetOrDefault<T>(this IConfiguration configuration, string key, T defaultValue)
		{
			var str = configuration[key] ?? configuration[key.Replace(".", string.Empty)];
			if(str == null)
				return defaultValue;
			if(typeof(T) == typeof(bool))
			{
				var trueValues = new[] { "1", "true" };
				var falseValues = new[] { "0", "false" };
				if(trueValues.Contains(str, StringComparer.OrdinalIgnoreCase))
					return (T)(object)true;
				if(falseValues.Contains(str, StringComparer.OrdinalIgnoreCase))
					return (T)(object)false;
				throw new FormatException();
			}
			else if(typeof(T) == typeof(Uri))
				return (T)(object)new Uri(str, UriKind.Absolute);
			else if(typeof(T) == typeof(string))
				return (T)(object)str;
			else if(typeof(T) == typeof(int))
			{
				return (T)(object)int.Parse(str, CultureInfo.InvariantCulture);
			}
			else if (typeof(T) == typeof(long))
			{
				return (T)(object)long.Parse(str, CultureInfo.InvariantCulture);
			}
			else
			{
				throw new NotSupportedException("Configuration value does not support time " + typeof(T).Name);
			}
		}
    }
}
