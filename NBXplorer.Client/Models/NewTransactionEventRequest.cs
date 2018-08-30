namespace NBXplorer.Models
{
	public class NewTransactionEventRequest : NewEventBase
	{
		public string[] DerivationSchemes { get; set; }
	}
}
