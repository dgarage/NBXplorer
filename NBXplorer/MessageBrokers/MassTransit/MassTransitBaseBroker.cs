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

		private IBusControl _bus;


		protected MassTransitBaseBroker(TConfiguration configuration)
		{
			_configuration = configuration;
			Init();
		}

		public async Task Send(NewTransactionEvent transactionEvent)
		{
			switch (_configuration.BroadcastType)
			{
				case BroadcastType.Publish:
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
			switch (_configuration.BroadcastType)
			{
				case BroadcastType.Publish:
					await _bus.Publish(blockEvent);
					break;
				case BroadcastType.Send:
					await _bus.Send(blockEvent);
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		public async Task Close()
		{
			await _bus.StopAsync();
		}

		private void Init()
		{
			_bus = CreateBus();
			if (!string.IsNullOrEmpty(_configuration.Endpoint))
			{

				EndpointConvention.Map<NewBlockEvent>(
					new Uri(_configuration.ConnectionString, _configuration.Endpoint));
				EndpointConvention.Map<NewTransactionEvent>(new Uri(_configuration.ConnectionString,
					_configuration.Endpoint));
			}

			_bus.Start();
		}

		protected abstract IBusControl CreateBus();
	}
}