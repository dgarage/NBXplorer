using MassTransit;
using Newtonsoft.Json;

namespace NBXplorer.MessageBrokers.MassTransit
{
	public class MassTransitRabbitMessageQueueBroker : MassTransitBaseBroker<MassTransitRabbitMessageQueueConfiguration>
	{
		private readonly MassTransitRabbitMessageQueueConfiguration _configuration;
		private readonly JsonSerializerSettings _jsonSerializerSettings;

		public MassTransitRabbitMessageQueueBroker(JsonSerializerSettings jsonSerializerSettings,
			MassTransitRabbitMessageQueueConfiguration configuration) : base(configuration)
		{
			_jsonSerializerSettings = jsonSerializerSettings;
			_configuration = configuration;
		}

		protected override IBusControl CreateBus()
		{
			return Bus.Factory.CreateUsingRabbitMq(cfg =>
			{
				cfg.Host(_configuration.ConnectionString, host =>
				{
					host.Username(_configuration.Username);
					host.Password(_configuration.Password);
				});
				cfg.ConfigureJsonDeserializer(settings => _jsonSerializerSettings);
				cfg.ConfigureJsonSerializer(settings => _jsonSerializerSettings);
			});
		}
	}
}