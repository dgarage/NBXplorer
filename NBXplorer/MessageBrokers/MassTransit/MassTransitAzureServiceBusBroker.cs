using System;
using MassTransit;
using MassTransit.AzureServiceBusTransport;
using Newtonsoft.Json;

namespace NBXplorer.MessageBrokers.MassTransit
{
	public class MassTransitAzureServiceBusBroker : MassTransitBaseBroker<MassTransitAzureServiceBusConfiguration>
	{
		private readonly MassTransitAzureServiceBusConfiguration _configuration;
		private readonly JsonSerializerSettings _jsonSerializerSettings;


		public MassTransitAzureServiceBusBroker(JsonSerializerSettings jsonSerializerSettings,
			MassTransitAzureServiceBusConfiguration configuration) : base(configuration)
		{
			_jsonSerializerSettings = jsonSerializerSettings;
			_configuration = configuration;
		}

		protected override IBusControl CreateBus()
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