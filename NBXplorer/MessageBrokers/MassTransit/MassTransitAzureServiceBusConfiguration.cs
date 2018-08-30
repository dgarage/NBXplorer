using System;

namespace NBXplorer.MessageBrokers.MassTransit
{
	public class MassTransitAzureServiceBusConfiguration : IMassTransitConfiguration
	{
		public Uri ConnectionString { get; set; }
		public BroadcastType BroadcastType { get; set; }
		public string Endpoint { get; set; }
	}
}