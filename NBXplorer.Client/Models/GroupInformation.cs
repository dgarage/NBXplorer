using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class GroupChild
	{
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public string CryptoCode { get; set; }
		public string TrackedSource { get; set; }
	}
	public class GroupInformation
	{
		public string TrackedSource { get; set; }
		public string GroupId { get; set; }
		public GroupChild[] Children { get; set; }

		public GroupChild AsGroupChild() => new () { TrackedSource = TrackedSource };
	}
}
