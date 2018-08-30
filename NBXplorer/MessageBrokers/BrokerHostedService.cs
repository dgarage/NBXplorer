using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBXplorer.Configuration;
using NBXplorer.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Internal;
using NBXplorer.MessageBrokers.MassTransit;

namespace NBXplorer.MessageBrokers
{
	public class BrokerHostedService : IHostedService
	{
		EventAggregator _EventAggregator;
		bool _Disposed = false;
		CompositeDisposable _subscriptions = new CompositeDisposable();
		IBrokerClient _senderBlock = null;
		IBrokerClient _senderTransactions = null;
		ExplorerConfiguration _config;
		JsonSerializerSettings _serializerSettings;

		public BrokerHostedService(BitcoinDWaitersAccessor waiters, ChainProvider chainProvider, EventAggregator eventAggregator, IOptions<ExplorerConfiguration> config, IOptions<MvcJsonOptions> jsonOptions)
		{
			_EventAggregator = eventAggregator;
			ChainProvider = chainProvider;
			Waiters = waiters.Instance;
			_config = config.Value;
			_serializerSettings = jsonOptions.Value.SerializerSettings;
		}

		public Task StartAsync(CancellationToken cancellationToken)
		{
			if(_Disposed)
				throw new ObjectDisposedException(nameof(BrokerHostedService));

			_senderBlock = CreateClientBlock();
			_senderTransactions = CreateClientTransaction();

			_subscriptions.Add(_EventAggregator.Subscribe<Events.NewBlockEvent>(async o =>
			{
				var chain = ChainProvider.GetChain(o.CryptoCode);
				if(chain == null)
					return;
				var block = chain.GetBlock(o.BlockId);
				if(block != null)
				{
					var nbe = new Models.NewBlockEvent()
					{
						CryptoCode = o.CryptoCode,
						Hash = block.Hash,
						Height = block.Height,
						PreviousBlockHash = block?.Previous
					};
					await _senderBlock.Send(nbe);
				}
			}));


			_subscriptions.Add(_EventAggregator.Subscribe<Events.NewTransactionMatchEvent>(async o =>
			{
				var network = Waiters.GetWaiter(o.CryptoCode);
				if(network == null)
					return;
				var chain = ChainProvider.GetChain(o.CryptoCode);
				if(chain == null)
					return;
				var txe = new Models.NewTransactionEvent()
				{
					CryptoCode = o.CryptoCode,
					DerivationStrategy = o.Match.DerivationStrategy,
					BlockId = o.BlockId,
					TransactionData = Utils.ToTransactionResult(true, chain, new[] { o.SavedTransaction }),
					Inputs = o.Match.Inputs,
					Outputs = o.Match.Outputs
				};
				await _senderTransactions.Send(txe);
			}));

			Logs.Configuration.LogInformation("Starting Azure Service Bus Message Broker");
			return Task.CompletedTask;
		}

		IBrokerClient CreateClientTransaction()
		{
			var brokers = new List<IBrokerClient>();

			if (_config.TransactionEventBrokers != null && _config.TransactionEventBrokers.Any())
			{
				brokers.AddRange(_config.TransactionEventBrokers.Select(CreateFromBrokerConfiguration));
			}
			if (_config.BlockEventBrokers != null && _config.BlockEventBrokers.Any())
			{
				brokers.AddRange(_config.BlockEventBrokers.Select(CreateFromBrokerConfiguration));
			}
			return new CompositeBroker(brokers);
		}

		IBrokerClient CreateClientBlock()
		{
			var brokers = new List<IBrokerClient>();

			if (_config.BlockEventBrokers != null && _config.BlockEventBrokers.Any())
			{
				brokers.AddRange(_config.BlockEventBrokers.Select(CreateFromBrokerConfiguration));
			}
			return new CompositeBroker(brokers);
		}

		IBrokerClient CreateFromBrokerConfiguration(BrokerConfiguration configuration)
		{
			switch (configuration.Broker)
			{
				case "asb":
					return configuration.BroadcastType == BroadcastType.Publish
						? CreateAzureTopic(configuration.ConnectionString, configuration.Endpoint)
						: CreateAzureQueue(configuration.ConnectionString, configuration.Endpoint);
				case "mt-asb":
					return CreateMassTransitClient(new MassTransitAzureServiceBusConfiguration()
					{
						Endpoint = configuration.Endpoint,
						ConnectionString = new Uri(configuration.ConnectionString),
						BroadcastType = configuration.BroadcastType
					});
				case "mt-rmq":
					return CreateMassTransitClient(new MassTransitRabbitMessageQueueConfiguration()
					{
						Endpoint = configuration.Endpoint,
						ConnectionString = new Uri(configuration.ConnectionString),
						BroadcastType = configuration.BroadcastType,
						Password = configuration.Password,
						Username = configuration.Username
					});
				default:
					throw  new ArgumentOutOfRangeException();
			}
		}

		private IBrokerClient CreateAzureQueue(string connnectionString, string queueName)
		{
			return new AzureBroker(new QueueClient(connnectionString, queueName), _serializerSettings);
		}

		private IBrokerClient CreateAzureTopic(string connectionString, string topicName)
		{
			return new AzureBroker(new TopicClient(connectionString, topicName), _serializerSettings);
		}

		public IBrokerClient CreateMassTransitClient(IMassTransitConfiguration configuration)
		{
			if (configuration is MassTransitAzureServiceBusConfiguration busConfiguration)
			{
				return new MassTransitAzureServiceBusBroker(_serializerSettings, busConfiguration);
			}
			else if (configuration is MassTransitRabbitMessageQueueConfiguration queueConfiguration)
			{
				return new MassTransitRabbitMessageQueueBroker(_serializerSettings, queueConfiguration);

			}
			throw new ArgumentOutOfRangeException();
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			_Disposed = true;
			_subscriptions.Dispose();
			await Task.WhenAll(_senderBlock.Close(), _senderTransactions.Close());
		}

		public ChainProvider ChainProvider
		{
			get; set;
		}
		public BitcoinDWaiters Waiters
		{
			get; set;
		}
	}
}