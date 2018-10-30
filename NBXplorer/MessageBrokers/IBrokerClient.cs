using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer.MessageBrokers
{
    public interface IBrokerClient
    {
		Task Send(Models.NewTransactionEvent transactionEvent);
		Task Send(Models.NewBlockEvent blockEvent);
		Task Close();
	}
}
