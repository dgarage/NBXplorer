using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using NBXplorer.Models;

namespace NBXplorer.Filters
{
	public class NBXplorerExceptionFilter : IExceptionFilter
	{
		public void OnException(ExceptionContext context)
		{
			NBXplorerException ex = null;
			var formatEx = context.Exception as FormatException;
			if(formatEx != null)
			{
				ex = new NBXplorerError(400, "invalid-format", formatEx.Message).AsException();
			}
			ex = ex ?? context.Exception as NBXplorerException;
			if(ex != null)
			{
				context.Exception = null;
				context.ExceptionDispatchInfo = null;
				context.ExceptionHandled = true;
				context.Result = new ObjectResult(ex.Error) { StatusCode = ex.Error.HttpCode };
			}
		}
	}
}
