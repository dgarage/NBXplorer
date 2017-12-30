using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
    public class NewBlockEvent
    {
		public int Height
		{
			get; set;
		}

		public uint256 Hash
		{
			get; set;
		}
	}
}
