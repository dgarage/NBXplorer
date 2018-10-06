using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using NBXplorer.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace NBXplorer.MessageBrokers
{
	public class AzureBroker : IBrokerClient
	{
		const int MaxMessageIdLength = 128;

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
			string msgIdHash = HashMessageId($"{transactionEvent.TrackedSource}-{transactionEvent.TransactionData.Transaction.GetHash()}-{(transactionEvent.TransactionData.BlockId?.ToString() ?? string.Empty)}");
			ValidateMessageId(msgIdHash);
			message.MessageId = msgIdHash;
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

		private string HashMessageId(string messageId)
		{
			HashAlgorithm algorithm = SHA256.Create();
			return Encoding.UTF8.GetString( algorithm.ComputeHash(Encoding.UTF8.GetBytes(messageId)));
		}

		private void ValidateMessageId(string messageId)
		{
			if (string.IsNullOrEmpty(messageId) )
			{
				throw new ArgumentException("MessageIdIsNullOrEmpty");
			}
			else if (messageId.Length > MaxMessageIdLength)
			{
				throw new ArgumentException($"MessageIdIsOverMaxLength ({MaxMessageIdLength}) :  {messageId} ");
			}
		}
	}
}
