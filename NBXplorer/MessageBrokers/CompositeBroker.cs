using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBXplorer.Models;

namespace NBXplorer.MessageBrokers
{
    public class CompositeBroker : IBrokerClient
    {
		public CompositeBroker(IEnumerable<IBrokerClient> clients)
		{
			Clients = clients.ToArray();
		}

		public IBrokerClient[] Clients
		{
			get;
		}

		public Task Close()
		{
			if(Clients.Length == 0)
				return Task.CompletedTask;
			return Task.WhenAll(Clients.Select(c => c.Close()));
		}
		
		public Task Send(NewTransactionEvent transactionEvent)
		{
			if(Clients.Length == 0)
				return Task.CompletedTask;
			return Task.WhenAll(Clients.Select(c => c.Send(transactionEvent)));
		}

		public Task Send(NewBlockEvent blockEvent)
		{
			if(Clients.Length == 0)
				return Task.CompletedTask;
			return Task.WhenAll(Clients.Select(c => c.Send(blockEvent)));
		}
	}
}
