using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using NBitcoin.DataEncoders;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Filters
{
	public class CookieFileAuthenticationFilter : Attribute, IAuthorizationFilter
	{
		public CookieFileAuthenticationFilter()
		{
		}
		public void OnAuthorization(AuthorizationFilterContext context)
		{
			if(!Authorized(context.HttpContext))
			{
				context.Result = new UnauthorizedResult();
				return;
			}
		}

		private bool Authorized(HttpContext httpContext)
		{

			RPCAuthorization authorization = (RPCAuthorization)httpContext.RequestServices.GetService(typeof(RPCAuthorization));
			if(!authorization.IsAuthorized(httpContext.Connection.RemoteIpAddress))
				return false;
			StringValues auth;
			if(!httpContext.Request.Headers.TryGetValue("Authorization", out auth) || auth.Count != 1)
				return false;
			var splittedAuth = auth[0].Split(' ');
			if(splittedAuth.Length != 2 ||
			   splittedAuth[0] != "Basic")
				return false;

			try
			{
				var user = Encoders.ASCII.EncodeData(Encoders.Base64.DecodeData(splittedAuth[1]));
				if(!authorization.IsAuthorized(user))
					return false;
			}
			catch
			{
				return false;
			}
			return true;
		}
	}
}
