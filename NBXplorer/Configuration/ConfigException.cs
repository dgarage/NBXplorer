using System;

namespace NBXplorer.Configuration
{
	public class ConfigException : Exception
	{
		public ConfigException() : base("")
		{

		}
		public ConfigException(string message) : base(message)
		{

		}
	}
}
