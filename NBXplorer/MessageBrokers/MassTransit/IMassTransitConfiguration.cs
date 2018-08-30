using System;

namespace NBXplorer.MessageBrokers.MassTransit
{
	public interface IMassTransitConfiguration
	{
		Uri ConnectionString { get; set; }
		BroadcastType BroadcastType { get; set; }
		string NewTransactionEventEndpoint { get; set; }
		string NewBlockEventEndpoint { get; set; }
	}
}