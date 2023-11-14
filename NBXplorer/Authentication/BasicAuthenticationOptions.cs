using Microsoft.AspNetCore.Authentication;

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
