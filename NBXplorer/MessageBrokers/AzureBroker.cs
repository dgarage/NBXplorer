using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using NBXplorer.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NBXplorer.MessageBrokers
{
    public class AzureBroker : IBrokerClient
	{
		public AzureBroker(ISenderClient client, JsonSerializerSettings serializerSettings)
		{
			Client = client;
			SerializerSettings = serializerSettings;
		}

		public ISenderClient Client
		{
			get;
		}
		public JsonSerializerSettings SerializerSettings
		{
			get;
		}

		static Encoding UTF8 = new UTF8Encoding(false);
		public async Task Send(NewTransactionEvent transactionEvent)
		{
			string jsonMsg = transactionEvent.ToJson(SerializerSettings);
			var bytes = UTF8.GetBytes(jsonMsg);
			var message = new Message(bytes);
			message.MessageId = HashCode.Combine(transactionEvent.DerivationStrategy.ToString(), transactionEvent.TransactionData.Transaction.GetHash(), transactionEvent.TransactionData.BlockId).ToString();
			message.ContentType = transactionEvent.GetType().ToString();
			await Client.SendAsync(message);
		}

		public async Task Send(NewBlockEvent blockEvent)
		{
			string jsonMsg = blockEvent.ToJson(SerializerSettings);
			var bytes = UTF8.GetBytes(jsonMsg);
			var message = new Message(bytes);
			message.MessageId = blockEvent.Hash.ToString();
			message.ContentType = blockEvent.GetType().ToString();
			await Client.SendAsync(message);
		}

		public async Task Close()
		{
			if(!Client.IsClosedOrClosing)
				await Client.CloseAsync();
		}
	}
}
