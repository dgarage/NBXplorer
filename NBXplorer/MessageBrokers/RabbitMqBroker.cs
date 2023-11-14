using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NBXplorer.Models;
using RabbitMQ.Client;

namespace NBXplorer.MessageBrokers
{
    internal class RabbitMqBroker : IBrokerClient
    {
        private readonly NBXplorerNetworkProvider Networks;
        private readonly ConnectionFactory ConnectionFactory;
        private readonly string NewTransactionExchange;
        private readonly string NewBlockExchange;
        
        private IConnection Connection;
        private IModel Channel;

        public RabbitMqBroker(
            NBXplorerNetworkProvider networks, ConnectionFactory connectionFactory, 
            string newTransactionExchange, string newBlockExchange)
        {
            Networks = networks;
            ConnectionFactory = connectionFactory;
            NewTransactionExchange = newTransactionExchange;
            NewBlockExchange = newBlockExchange;
        }

        private void CheckAndOpenConnection()
        {
            if(Channel == null) 
            {
                Connection = ConnectionFactory.CreateConnection();
                Channel = Connection.CreateModel();

                if(!string.IsNullOrEmpty(NewTransactionExchange)) 
                    Channel.ExchangeDeclare(NewTransactionExchange, ExchangeType.Topic);
                if(!string.IsNullOrEmpty(NewBlockExchange)) 
                    Channel.ExchangeDeclare(NewBlockExchange, ExchangeType.Topic);
            }
        }

        Task IBrokerClient.Close()
        {
            if(Connection != null && Connection.IsOpen)
                Connection.Close();
            if(Channel != null && Channel.IsOpen)
                Channel.Close();

            return Task.CompletedTask;
        }

        Task IBrokerClient.Send(NewTransactionEvent transactionEvent)
        {
            CheckAndOpenConnection();

            string jsonMsg = transactionEvent.ToJson(Networks.GetFromCryptoCode(transactionEvent.CryptoCode).JsonSerializerSettings);
            var body = Encoding.UTF8.GetBytes(jsonMsg);
            
            var conf = (transactionEvent.BlockId == null ? "unconfirmed" : "confirmed");
            var routingKey = $"transactions.{transactionEvent.CryptoCode}.{conf}";
            
            string msgIdHash = HashMessageId($"{transactionEvent.TrackedSource}-{transactionEvent.TransactionData.Transaction.GetHash()}-{(transactionEvent.TransactionData.BlockId?.ToString() ?? string.Empty)}");
			ValidateMessageId(msgIdHash);

            IBasicProperties props = Channel.CreateBasicProperties();
            props.MessageId = msgIdHash;
            props.ContentType = typeof(NewTransactionEvent).ToString();
            props.Headers = new Dictionary<string, object>();
            props.Headers.Add("CryptoCode", transactionEvent.CryptoCode);

            Channel.BasicPublish(
                exchange: NewTransactionExchange, 
                routingKey: routingKey,
                basicProperties: props, 
                body: body);

            return Task.CompletedTask;
        }

        Task IBrokerClient.Send(NewBlockEvent blockEvent)
        {
            CheckAndOpenConnection();

            string jsonMsg = blockEvent.ToJson(Networks.GetFromCryptoCode(blockEvent.CryptoCode).JsonSerializerSettings);
            var body = Encoding.UTF8.GetBytes(jsonMsg);

            var routingKey = $"blocks.{blockEvent.CryptoCode}";
            
            IBasicProperties props = Channel.CreateBasicProperties();
            props.MessageId = blockEvent.Hash.ToString();
            props.ContentType = typeof(NewBlockEvent).ToString();
            props.Headers = new Dictionary<string, object>();
            props.Headers.Add("CryptoCode", blockEvent.CryptoCode);

            Channel.BasicPublish(
                exchange: NewBlockExchange, 
                routingKey: routingKey,
                basicProperties: props, 
                body: body);

            return Task.CompletedTask;
        }

        const int MaxMessageIdLength = 128;
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