using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class LockInfoResponse
	{
		public OutPoint[] LockedOutpoints { get; set; }
	}
}
