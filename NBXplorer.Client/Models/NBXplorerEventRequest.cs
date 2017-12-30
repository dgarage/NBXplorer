using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
    public class NBXplorerEventRequest
    {
		public NBXplorerEventType Type
		{
			get; set;
		}

		public NBXplorerEventRequest()
		{

		}
		public NBXplorerEventRequest(NewBlockEventRequest newBlockRequest)
		{
			if(newBlockRequest == null)
				throw new ArgumentNullException(nameof(newBlockRequest));
			Type = NBXplorerEventType.NewBlock;
			NewBlockRequest = newBlockRequest;
		}

		public NewBlockEventRequest NewBlockRequest
		{
			get;
			set;
		}
	}
}
