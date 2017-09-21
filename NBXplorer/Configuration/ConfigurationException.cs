using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer.Configuration
{
	public class ConfigurationException : Exception
	{
		public ConfigurationException(string msg) : base(msg)
		{

		}
	}
}
