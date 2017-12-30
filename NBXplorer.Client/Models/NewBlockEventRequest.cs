using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
    public class NewBlockEventRequest
    {
		public NewBlockEventRequest()
		{

		}
		public NewBlockEventRequest(BlockLocator blockLocator)
		{
			BlockLocator = blockLocator;

		}
		public BlockLocator BlockLocator
		{
			get; set;
		}
	}
}
