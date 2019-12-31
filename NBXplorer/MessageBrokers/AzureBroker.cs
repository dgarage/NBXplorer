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

		public AzureBroker(ISenderClient client, NBXplorerNetworkProvider networks)
		{
			Client = client;
			Networks = networks;
		}

		public ISenderClient Client
		{
			get;
		}
		public NBXplorerNetworkProvider Networks { get; }

		static Encoding UTF8 = new UTF8Encoding(false);
		public async Task Send(NewTransactionEvent transactionEvent)
		{
			string jsonMsg = transactionEvent.ToJson(Networks.GetFromCryptoCode(transactionEvent.CryptoCode).JsonSerializerSettings);
			var bytes = UTF8.GetBytes(jsonMsg);
			var message = new Message(bytes);
			string msgIdHash = HashMessageId($"{transactionEvent.TrackedSource}-{transactionEvent.TransactionData.Transaction.GetHash()}-{(transactionEvent.TransactionData.BlockId?.ToString() ?? string.Empty)}");
			ValidateMessageId(msgIdHash);
			message.MessageId = msgIdHash;
			message.ContentType = transactionEvent.GetType().ToString();
			message.UserProperties.Add("CryptoCode", transactionEvent.CryptoCode);
			await Client.SendAsync(message);
		}

		public async Task Send(NewBlockEvent blockEvent)
		{
			string jsonMsg = blockEvent.ToJson(Networks.GetFromCryptoCode(blockEvent.CryptoCode).JsonSerializerSettings);
			var bytes = UTF8.GetBytes(jsonMsg);
			var message = new Message(bytes);
			message.MessageId = blockEvent.Hash.ToString();
			message.ContentType = blockEvent.GetType().ToString();
			message.UserProperties.Add("CryptoCode", blockEvent.CryptoCode);			
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
