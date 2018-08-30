using System;

namespace NBXplorer.MessageBrokers.MassTransit
{
	public class MassTransitAzureServiceBusConfiugration : IMassTransitConfiguration
	{
		public Uri ConnectionString { get; set; }
		public BroadcastType BroadcastType { get; set; }
		public string NewTransactionEventEndpoint { get; set; }
		public string NewBlockEventEndpoint { get; set; }
	}
}