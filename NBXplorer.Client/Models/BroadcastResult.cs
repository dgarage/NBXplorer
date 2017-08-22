using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin.RPC;

namespace NBXplorer.Models
{
    public class BroadcastResult
    {
		public BroadcastResult()
		{

		}
		public BroadcastResult(bool result)
		{
			Success = result;
		}
		public bool Success
		{
			get; set;
		}
		public RPCErrorCode? RPCCode
		{
			get;
			set;
		}
		public string RPCCodeMessage
		{
			get;
			set;
		}
		public string RPCMessage
		{
			get;
			set;
		}
	}
}
