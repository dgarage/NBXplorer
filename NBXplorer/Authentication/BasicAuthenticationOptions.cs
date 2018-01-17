using Microsoft.AspNetCore.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer.Authentication
{
	public class BasicAuthenticationOptions : AuthenticationSchemeOptions
	{
		public BasicAuthenticationOptions():base()
		{
		}
		public string Username
		{
			get; set;
		}

		public string Password
		{
			get; set;
		}
	}
}
