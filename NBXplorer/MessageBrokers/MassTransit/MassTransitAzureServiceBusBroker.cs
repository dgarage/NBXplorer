using System;
using MassTransit;
using MassTransit.AzureServiceBusTransport;
using Newtonsoft.Json;

namespace NBXplorer.MessageBrokers.MassTransit
{
	public class MassTransitAzureServiceBusBroker : MassTransitBaseBroker<MassTransitAzureServiceBusConfiugration>
	{
		private readonly JsonSerializerSettings _jsonSerializerSettings;
		private readonly MassTransitAzureServiceBusConfiugration _configuration;


		public MassTransitAzureServiceBusBroker(JsonSerializerSettings jsonSerializerSettings,
			MassTransitAzureServiceBusConfiugration configuration) : base(configuration)
		{
			_jsonSerializerSettings = jsonSerializerSettings;
			_configuration = configuration;
		}

		protected override IBus CreateBus()
		{
			return Bus.Factory.CreateUsingAzureServiceBus(cfg =>
			{
				cfg.Host(_configuration.ConnectionString, host => { host.OperationTimeout = TimeSpan.FromSeconds(5); });
				cfg.ConfigureJsonDeserializer(settings => _jsonSerializerSettings);
				cfg.ConfigureJsonSerializer(settings => _jsonSerializerSettings);
			});
		}
	}
}