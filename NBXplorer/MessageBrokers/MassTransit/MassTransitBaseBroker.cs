using System;
using System.Threading.Tasks;
using MassTransit;
using NBXplorer.Models;

namespace NBXplorer.MessageBrokers.MassTransit
{
	public abstract class MassTransitBaseBroker<TConfiguration> : IBrokerClient
		where TConfiguration : IMassTransitConfiguration
	{
		private readonly TConfiguration _configuration;

		private IBus _bus;


		protected MassTransitBaseBroker(TConfiguration configuration)
		{
			_configuration = configuration;
			Init();
		}

		public async Task Send(NewTransactionEvent transactionEvent)
		{
			switch (_configuration.BroadcastType)
			{
				case BroadcastType.Pubish:
					await _bus.Publish(transactionEvent);
					break;
				case BroadcastType.Send:
					await _bus.Send(transactionEvent);
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		public async Task Send(NewBlockEvent blockEvent)
		{
			await _bus.Publish(blockEvent);
		}

		public Task Close()
		{
			return Task.CompletedTask;
		}

		private void Init()
		{
			_bus = CreateBus();
			if (!string.IsNullOrEmpty(_configuration.NewBlockEventEndpoint))
				EndpointConvention.Map<NewBlockEvent>(
					new Uri(_configuration.ConnectionString, _configuration.NewBlockEventEndpoint));
			if (!string.IsNullOrEmpty(_configuration.NewTransactionEventEndpoint))
				EndpointConvention.Map<NewTransactionEvent>(new Uri(_configuration.ConnectionString,
					_configuration.NewTransactionEventEndpoint));
		}

		protected abstract IBus CreateBus();
	}
}