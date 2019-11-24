using System.Collections.Generic;
using System.Text;
using System.Text.Unicode;
using System.Threading.Tasks;
using NBXplorer.Models;
using RabbitMQ.Client;

namespace NBXplorer.MessageBrokers
{
    internal class RabbitMqBroker : IBrokerClient
    {
        private string NewTransactionExchange;
        private IConnection Connection;
        private IModel Channel;

        public RabbitMqBroker(IConnection connection, string newTransactionExchange, NBXplorerNetworkProvider networks)
        {
            Connection = connection;
            NewTransactionExchange = newTransactionExchange;
            Networks = networks;
            Channel = connection.CreateModel();
        }

        public NBXplorerNetworkProvider Networks { get; }

        Task IBrokerClient.Close()
        {
            throw new System.NotImplementedException();
        }

        Task IBrokerClient.Send(NewTransactionEvent transactionEvent)
        {
            string jsonMsg = transactionEvent.ToJson(Networks.GetFromCryptoCode(transactionEvent.CryptoCode).JsonSerializerSettings);
            var body = Encoding.UTF8.GetBytes(jsonMsg);
            
            var conf = (transactionEvent.BlockId == null ? "unconfirmed" : "confirmed");
            var routingKey = $"transactions.{transactionEvent.CryptoCode}.{conf}";
            
            IBasicProperties props = Channel.CreateBasicProperties();
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
            string jsonMsg = blockEvent.ToJson(Networks.GetFromCryptoCode(blockEvent.CryptoCode).JsonSerializerSettings);
            var body = Encoding.UTF8.GetBytes(jsonMsg);

            // TODO: Add routing key for crypto code
            
            IBasicProperties props = Channel.CreateBasicProperties();
            props.Headers.Add("CryptoCode", blockEvent.CryptoCode);

            Channel.BasicPublish(
                exchange: NewTransactionExchange, 
                routingKey: blockEvent.CryptoCode,
                basicProperties: null, 
                body: body);

            return Task.CompletedTask;
        }
    }
}