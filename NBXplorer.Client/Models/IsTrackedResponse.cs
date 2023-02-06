using NBXplorer.Models;

namespace NBXplorer.Models
{
	public class IsTrackedResponse
	{
		public bool IsTracked { get; set; }
		public TrackedSource TrackedSource { get; set; }
	}
}