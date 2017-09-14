using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using NBitcoin.DataEncoders;
using NBXplorer.Filters;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class NBXplorerMiddleware
	{
		RPCAuthorization authorization;
		RequestDelegate _Next;
		public NBXplorerMiddleware(RequestDelegate next, RPCAuthorization authorization)
		{
			if(authorization == null)
				throw new ArgumentNullException(nameof(authorization));
			if(next == null)
				throw new ArgumentNullException(nameof(next));
			this.authorization = authorization;
			_Next = next;
		}

		public Task Invoke(HttpContext httpContext)
		{
			if(httpContext == null)
				throw new ArgumentNullException(nameof(httpContext));


			if(!this.Authorized(httpContext))
			{
				httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
				return Task.CompletedTask;
			}
			return _Next(httpContext);
		}

		private bool Authorized(HttpContext httpContext)
		{
			if(!this.authorization.IsAuthorized(httpContext.Connection.RemoteIpAddress))
				return false;
			string user = null;

			if(httpContext.Request.Headers.TryGetValue("Authorization", out StringValues auth) && auth.Count == 1)
			{
				var splittedAuth = auth[0].Split(' ');
				if(splittedAuth.Length == 2 &&
				   splittedAuth[0] == "Basic")
				{
					try
					{
						user = Encoders.ASCII.EncodeData(Encoders.Base64.DecodeData(splittedAuth[1]));
					}
					catch { }
				}
			}

			if(!this.authorization.IsAuthorized(user))
				return false;

			return true;
		}
	}
}
