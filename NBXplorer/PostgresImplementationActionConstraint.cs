using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace NBXplorer
{
	/// <summary>
	/// Activate or deactivate a route if the postgres controller implementation should be used
	/// </summary>
	public class PostgresImplementationActionConstraint : Attribute, IActionConstraint
	{
		public PostgresImplementationActionConstraint(bool postgresImplementation)
		{
			PostgresImplementation = postgresImplementation;
		}
		public int Order => 100;

		public bool PostgresImplementation { get; }

		public bool Accept(ActionConstraintContext context)
		{
			var conf = context.RouteContext.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
			return string.IsNullOrEmpty(conf["POSTGRES"]) == !PostgresImplementation;
		}
	}
}
