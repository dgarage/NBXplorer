using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
    public class NewTransactionEventRequest
    {
		public NewTransactionEventRequest()
		{

		}
		public string CryptoCode
		{
			get; set;
		}
		public string[] DerivationSchemes
		{
			get; set;
		}
	}
}
