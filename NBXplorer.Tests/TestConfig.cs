namespace NBXplorer.Tests
{
	public static class AzureServiceBusTestConfig
	{
		public static string ConnectionString
		{
			get
			{
				//Put your service bus connection string here - requires READ / WRITE permissions
				return "Endpoint=sb://prodwhalelend.servicebus.windows.net/;SharedAccessKeyName=nbxplorer;SharedAccessKey=p+QEv6F1E7znNuFYHtD6U86878mF7rx45NJBY7S3Jqs=";
			}
		}

		public static string NewBlockQueue
		{
			get
			{
				return "newblock";
			}
		}
		public static string NewBlockTopic
		{
			get
			{
				return "newbitcoinblock";
			}
		}

		public static string NewBlockSubscription
		{
			get
			{
				return "NewBlock";
			}
		}

		public static string NewTransactionQueue
		{
			get
			{
				return "newtransaction";
			}
		}

		public static string NewTransactionTopic
		{
			get
			{
				return "newbitcointransaction";
			}
		}

		public static string NewTransactionSubscription
		{
			get
			{
				return "NewTransaction";
			}
		}

	}
}
