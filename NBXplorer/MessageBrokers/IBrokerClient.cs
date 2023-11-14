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
