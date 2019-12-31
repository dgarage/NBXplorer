namespace NBXplorer.Models
{
	public class NewBlockEventRequest : NewEventBase
	{
		public override string EventType => "subscribeblock";
	}
}
