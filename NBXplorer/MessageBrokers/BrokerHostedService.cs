﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBXplorer.Configuration;
using NBXplorer.Logging;
using Newtonsoft.Json;
using RabbitMQ.Client;
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

		public BrokerHostedService(BitcoinDWaiters waiters, ChainProvider chainProvider, EventAggregator eventAggregator, IOptions<ExplorerConfiguration> config, NBXplorerNetworkProvider networks)
		{
			_EventAggregator = eventAggregator;
			Networks = networks;
			ChainProvider = chainProvider;
			Waiters = waiters;
			_config = config.Value;
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
			if(!string.IsNullOrEmpty(_config.RabbitMqHostName) && 
				!string.IsNullOrEmpty(_config.RabbitMqUsername) && 
				!string.IsNullOrEmpty(_config.RabbitMqPassword)) 
			{
				if(!string.IsNullOrEmpty(_config.RabbitMqTransactionExchange)) 
				{
					brokers.Add(CreateRabbitMqExchange(
						hostName: _config.RabbitMqHostName,
						virtualHost: _config.RabbitMqVirtualHost,
						username: _config.RabbitMqUsername,
						password: _config.RabbitMqPassword,
						newTransactionExchange: _config.RabbitMqTransactionExchange,
						newBlockExchange: string.Empty));
				}
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

			if(!string.IsNullOrEmpty(_config.RabbitMqHostName) && 
				!string.IsNullOrEmpty(_config.RabbitMqUsername) && 
				!string.IsNullOrEmpty(_config.RabbitMqPassword)) 
			{
				if(!string.IsNullOrEmpty(_config.RabbitMqBlockExchange)) 
				{
					brokers.Add(CreateRabbitMqExchange(
						hostName: _config.RabbitMqHostName,
						virtualHost: _config.RabbitMqVirtualHost,
						username: _config.RabbitMqUsername,
						password: _config.RabbitMqPassword,
						newTransactionExchange: string.Empty,
						newBlockExchange: _config.RabbitMqBlockExchange));
				}
			}
			return new CompositeBroker(brokers);
		}

		private IBrokerClient CreateAzureQueue(string connnectionString, string queueName)
		{
			return new AzureBroker(new QueueClient(connnectionString, queueName), Networks);
		}

		private IBrokerClient CreateAzureTopic(string connectionString, string topicName)
		{
			return new AzureBroker(new TopicClient(connectionString, topicName), Networks);
		}

		private IBrokerClient CreateRabbitMqExchange(
			string hostName, string virtualHost, 
			string username, string password, 
			string newTransactionExchange, string newBlockExchange)
		{
			return new RabbitMqBroker(
				Networks,
				new ConnectionFactory() { 
					HostName = hostName, VirtualHost = virtualHost,
					UserName = username, Password = password }, 
				newTransactionExchange, newBlockExchange );
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
		public NBXplorerNetworkProvider Networks { get; }
		public BitcoinDWaiters Waiters
		{
			get; set;
		}
	}
}