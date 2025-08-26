namespace NBXplorer;


public class ListenEventsRequest
{
	public enum RuleAction
	{
		Allow,
		Reject
	}
	public class ListenRule
	{
		public enum EventType
		{
			NewBlock,
			NewTransaction
		}
		public string CryptoCode { get; set; }
		public bool Inverse { get; set; }
		public EventType? Type { get; set; }
		public RuleAction Action { get; set; }
		public string TrackedSource { get; set; }
		public string DerivationScheme { get; set; }
	}
	public ListenRule[] Rules { get; set; }
	public RuleAction DefaultAction { get; set; }
}
