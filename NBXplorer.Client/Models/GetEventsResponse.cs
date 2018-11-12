using NBXplorer.Models;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class GetEventsResponse
	{
		public long LastEventId { get; set; }

		public NewEventBase[] Events { get; set; }
	}
}
