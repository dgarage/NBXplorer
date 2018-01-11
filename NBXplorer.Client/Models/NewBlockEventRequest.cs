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

		public String CryptoCode
		{
			get; set;
		}
	}
}
