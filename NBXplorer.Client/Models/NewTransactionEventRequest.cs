namespace NBXplorer.Models
{
	public class NewTransactionEventRequest : NewEventBase
	{
		public override string EventType => "subscribetransaction";
		public string[] DerivationSchemes { get; set; }

		public string[] TrackedSources { get; set; }
		public bool? ListenAllTrackedSource { get; set; }
		public bool? ListenAllDerivationSchemes { get; set; }
	}
}
