using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;

namespace NBXplorer.Configuration
{
    public static class ConfigurationExtensions
    {
		class FallbackConfiguration : IConfiguration
		{
			private IConfiguration _Configuration;
			private IConfiguration _Fallback;

			public FallbackConfiguration(IConfiguration configuration, IConfiguration fallback)
			{
				_Configuration = configuration;
				_Fallback = fallback;
			}
			public string this[string key] { get => _Configuration[key] ?? _Fallback[key]; set => throw new NotSupportedException(); }

			public IEnumerable<IConfigurationSection> GetChildren()
			{
				return _Configuration.GetChildren();
			}

			public IChangeToken GetReloadToken()
			{
				return _Configuration.GetReloadToken();
			}

			public IConfigurationSection GetSection(string key)
			{
				return _Configuration.GetSection(key);
			}
		}

		public static string[] GetAll(this IConfiguration config, string key)
		{
			var data = config.GetOrDefault<string>(key, null);
			if(data == null)
				return new string[0];
			return new string[] { data };
		}
		public static IConfiguration AddFallback(this IConfiguration configuration, IConfiguration fallback)
		{
			return new FallbackConfiguration(configuration, fallback);
		}
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
			else if(typeof(T) == typeof(IPEndPoint))
			{
				var separator = str.LastIndexOf(":");
				if(separator == -1)
					throw new FormatException();
				var ip = str.Substring(0, separator);
				var port = str.Substring(separator + 1);
				return (T)(object)new IPEndPoint(IPAddress.Parse(ip), int.Parse(port));
			}
			else if(typeof(T) == typeof(int))
			{
				return (T)(object)int.Parse(str, CultureInfo.InvariantCulture);
			}
			else
			{
				throw new NotSupportedException("Configuration value does not support time " + typeof(T).Name);
			}
		}
    }
}
