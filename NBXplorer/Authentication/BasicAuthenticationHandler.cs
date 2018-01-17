using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace NBXplorer.Authentication
{
	public class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationOptions>
	{
		public BasicAuthenticationHandler(IOptionsMonitor<BasicAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock) : base(options, logger, encoder, clock)
		{
		}

		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			var authHeader = (string)this.Request.Headers["Authorization"];

			if(Options.Username == null)
			{
				var user = new GenericPrincipal(new GenericIdentity("Anonymous"), null);
				var ticket = new AuthenticationTicket(user, new AuthenticationProperties(), "Basic");
				return Task.FromResult(AuthenticateResult.Success(ticket));
			}

			if(!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("basic", StringComparison.OrdinalIgnoreCase))
			{
				//Extract credentials
				string encodedUsernamePassword = authHeader.Substring("Basic ".Length).Trim();
				Encoding encoding = Encoding.GetEncoding("iso-8859-1");
				string usernamePassword = encoding.GetString(Convert.FromBase64String(encodedUsernamePassword));

				int seperatorIndex = usernamePassword.IndexOf(':');

				var username = usernamePassword.Substring(0, seperatorIndex);
				var password = usernamePassword.Substring(seperatorIndex + 1);

				if(username.Equals(Options.Username, StringComparison.OrdinalIgnoreCase) && password.Equals(Options.Password, StringComparison.OrdinalIgnoreCase))
				{
					var user = new GenericPrincipal(new GenericIdentity(Options.Username), null);
					var ticket = new AuthenticationTicket(user, new AuthenticationProperties(), "Basic");
					return Task.FromResult(AuthenticateResult.Success(ticket));
				}
			}

			return Task.FromResult(AuthenticateResult.Fail("No valid user."));
		}
	}
}
