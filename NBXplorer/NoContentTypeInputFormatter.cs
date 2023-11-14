using Microsoft.AspNetCore.Mvc.Formatters;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class NoContentTypeInputFormatter : IInputFormatter
	{
		public bool CanRead(InputFormatterContext context)
		{
			return string.IsNullOrEmpty(context.HttpContext.Request.ContentType);
		}

		public Task<InputFormatterResult> ReadAsync(InputFormatterContext context)
		{
			return InputFormatterResult.NoValueAsync();
		}
	}
}
