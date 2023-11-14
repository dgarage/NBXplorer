using System;

namespace NBXplorer.Models
{
	public class NBXplorerException : Exception
	{
		public NBXplorerException(NBXplorerError error):base(error.Message)
		{
			Error = error;
		}

		public NBXplorerError Error
		{
			get; set;
		}
	}
	public class NBXplorerError
	{
		public NBXplorerError()
		{

		}
		public NBXplorerError(int httpCode, string code, string message)
		{
			HttpCode = httpCode;
			Code = code;
			Message = message;
		}
		public NBXplorerError(int httpCode, string code, string message, string reason)
		{
			HttpCode = httpCode;
			Code = code;
			Message = message;
			Reason = reason;
		}
		public int HttpCode
		{
			get; set;
		}
		public string Code
		{
			get; set;
		}
		public string Message
		{
			get; set;
		}
		public string Reason
		{
			get; set;
		}

		public NBXplorerException AsException()
		{
			return new NBXplorerException(this);
		}
	}
}
