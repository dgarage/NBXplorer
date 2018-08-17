using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Tests
{
	public static class AzureServiceBusTestConfig
	{
		public static string ConnectionString
		{
			get
			{
				//Put your service bus connection string here - requires READ / WRITE permissions
				return "";
			}
		}

		public static string NewBlockQueue
		{
			get
			{
				return "newblock";
			}
		}

		public static string NewTransactionQueue
		{
			get
			{
				return "newtransaction";
			}
		}
	}
}
