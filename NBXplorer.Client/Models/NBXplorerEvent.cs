using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public enum NBXplorerEventType
	{
		NewBlock,
	}

	public class NBXplorerEvent
	{
		public NBXplorerEvent()
		{

		}
		public NBXplorerEvent(NewBlockEvent newBlock)
		{
			if(newBlock == null)
				throw new ArgumentNullException(nameof(newBlock));
			Type = NBXplorerEventType.NewBlock;
			NewBlock = newBlock;
		}
		public NBXplorerEventType Type
		{
			get; set;
		}
		public NewBlockEvent NewBlock
		{
			get; set;
		}
	}
}
