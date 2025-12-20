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
        private IChannel Channel;

        public RabbitMqBroker(
            NBXplorerNetworkProvider networks, ConnectionFactory connectionFactory, 
            string newTransactionExchange, string newBlockExchange)
        {
            Networks = networks;
            ConnectionFactory = connectionFactory;
            NewTransactionExchange = newTransactionExchange;
            NewBlockExchange = newBlockExchange;
        }

        private async Task CheckAndOpenConnection()
        {
            if(Channel == null) 
            {
                Connection = await ConnectionFactory.CreateConnectionAsync();
                Channel = await Connection.CreateChannelAsync();

                if(!string.IsNullOrEmpty(NewTransactionExchange)) 
                    await Channel.ExchangeDeclareAsync(NewTransactionExchange, ExchangeType.Topic);
                if(!string.IsNullOrEmpty(NewBlockExchange)) 
                    await Channel.ExchangeDeclareAsync(NewBlockExchange, ExchangeType.Topic);
            }
        }

        async Task IBrokerClient.Close()
        {
            if(Connection != null && Connection.IsOpen)
                await Connection.CloseAsync();
            if(Channel != null && Channel.IsOpen)
                await Channel.CloseAsync();
        }

        async Task IBrokerClient.Send(NewTransactionEvent transactionEvent)
        {
            await CheckAndOpenConnection();

            string jsonMsg = transactionEvent.ToJson(Networks.GetFromCryptoCode(transactionEvent.CryptoCode).JsonSerializerSettings);
            var body = Encoding.UTF8.GetBytes(jsonMsg);
            
            var conf = (transactionEvent.BlockId == null ? "unconfirmed" : "confirmed");
            var routingKey = $"transactions.{transactionEvent.CryptoCode}.{conf}";
            
            string msgIdHash = HashMessageId($"{transactionEvent.TrackedSource}-{transactionEvent.TransactionData.Transaction.GetHash()}-{(transactionEvent.TransactionData.BlockId?.ToString() ?? string.Empty)}");
			ValidateMessageId(msgIdHash);

            var props = new BasicProperties();
            props.MessageId = msgIdHash;
            props.ContentType = typeof(NewTransactionEvent).ToString();
            props.Headers = new Dictionary<string, object>();
            props.Headers.Add("CryptoCode", transactionEvent.CryptoCode);

            await Channel.BasicPublishAsync(
                exchange: NewTransactionExchange, 
                routingKey: routingKey,
                mandatory: false,
                basicProperties: props, 
                body: body);
        }

        async Task IBrokerClient.Send(NewBlockEvent blockEvent)
        {
            await CheckAndOpenConnection();

            string jsonMsg = blockEvent.ToJson(Networks.GetFromCryptoCode(blockEvent.CryptoCode).JsonSerializerSettings);
            var body = Encoding.UTF8.GetBytes(jsonMsg);

            var routingKey = $"blocks.{blockEvent.CryptoCode}";
            
            var props = new BasicProperties();
            props.MessageId = blockEvent.Hash.ToString();
            props.ContentType = typeof(NewBlockEvent).ToString();
            props.Headers = new Dictionary<string, object>();
            props.Headers.Add("CryptoCode", blockEvent.CryptoCode);

            await Channel.BasicPublishAsync(
                exchange: NewBlockExchange, 
                routingKey: routingKey,
                mandatory: false,
                basicProperties: props, 
                body: body);
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