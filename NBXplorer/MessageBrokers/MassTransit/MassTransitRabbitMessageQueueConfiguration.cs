using System;

namespace NBXplorer.MessageBrokers.MassTransit
{
	public class MassTransitRabbitMessageQueueConfiguration : IMassTransitConfiguration
	{
		public string Username { get; set; }
		public string Password { get; set; }
		public Uri ConnectionString { get; set; }
		public BroadcastType BroadcastType { get; set; }
		public string Endpoint { get; set; }
	}
}