using NBXplorer.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon;

namespace NBXplorer.MessageBrokers
{
    public class AWSBroker : IBrokerClient
    {
        public AWSBroker(string queueURL, JsonSerializerSettings serializerSettings)
        {
            Client = new AmazonSQSClient();
            SerializerSettings = serializerSettings;
            QueueURL = queueURL;
        }

        public AmazonSQSClient Client
        {
            get;
        }
        public JsonSerializerSettings SerializerSettings
        {
            get;
        }

        public string QueueURL
        {
            get; set;
        }

        public Task Close()
        {
            return Task.CompletedTask;
        }

        public async Task Send(NewTransactionEvent transactionEvent)
        {
            string jsonMsg = transactionEvent.ToJson(SerializerSettings);
            await Client.SendMessageAsync(QueueURL, jsonMsg);
        }

        public async Task Send(NewBlockEvent blockEvent)
        {
            string jsonMsg = blockEvent.ToJson(SerializerSettings);
            await Client.SendMessageAsync(QueueURL, jsonMsg);
        }
    }


}