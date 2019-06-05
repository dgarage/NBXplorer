using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NBXplorer.Logging
{
	public class SILOLogAllRequestsMiddleware
	{
		private readonly RequestDelegate _next;

		public SILOLogAllRequestsMiddleware(RequestDelegate next)
		{
			_next = next;
		}

		public async Task Invoke(HttpContext context)
		{
			await _next(context);
			Logs.Requests.LogInformation($"{context.Connection.RemoteIpAddress},-,{context.Request.Method},{context.Request.Path}{context.Request.QueryString},{context.Response.StatusCode},{context.Request.Headers["User-Agent"].ToString()}");
		}
	}
}
