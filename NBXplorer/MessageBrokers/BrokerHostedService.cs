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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
			if (_Disposed)
				throw new ObjectDisposedException(nameof(BrokerHostedService));

			_senderBlock = CreateClientBlock();
			_senderTransactions = CreateClientTransaction();

			_subscriptions.Add(_EventAggregator.Subscribe<Models.NewBlockEvent>(async o =>
			{
				await _senderBlock.Send(o);
			}));


			_subscriptions.Add(_EventAggregator.Subscribe<Models.NewTransactionEvent>(async o =>
			{
				await _senderTransactions.Send(o);
			}));
			return Task.CompletedTask;
		}

		IBrokerClient CreateClientTransaction()
		{
			var brokers = new List<IBrokerClient>();
			if (!string.IsNullOrEmpty(_config.AzureServiceBusConnectionString))
			{
				if (!string.IsNullOrWhiteSpace(_config.AzureServiceBusTransactionQueue))
					brokers.Add(CreateAzureQueue(_config.AzureServiceBusConnectionString, _config.AzureServiceBusTransactionQueue));
				if (!string.IsNullOrWhiteSpace(_config.AzureServiceBusTransactionTopic))
					brokers.Add(CreateAzureTopic(_config.AzureServiceBusConnectionString, _config.AzureServiceBusTransactionTopic));
			}
			return new CompositeBroker(brokers);
		}

		IBrokerClient CreateClientBlock()
		{
			var brokers = new List<IBrokerClient>();
			if (!string.IsNullOrEmpty(_config.AzureServiceBusConnectionString))
			{
				if (!string.IsNullOrWhiteSpace(_config.AzureServiceBusBlockQueue))
					brokers.Add(CreateAzureQueue(_config.AzureServiceBusConnectionString, _config.AzureServiceBusBlockQueue));
				if (!string.IsNullOrWhiteSpace(_config.AzureServiceBusBlockTopic))
					brokers.Add(CreateAzureTopic(_config.AzureServiceBusConnectionString, _config.AzureServiceBusBlockTopic));
			}
			return new CompositeBroker(brokers);
		}

		private IBrokerClient CreateAzureQueue(string connnectionString, string queueName)
		{
			return new AzureBroker(new QueueClient(connnectionString, queueName), _serializerSettings);
		}

		private IBrokerClient CreateAzureTopic(string connectionString, string topicName)
		{
			return new AzureBroker(new TopicClient(connectionString, topicName), _serializerSettings);
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