using NBitcoin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NBXplorer.Configuration;

namespace NBXplorer.Configuration
{
	public class NBXplorerNetwork
	{

		public Network NBitcoinNetwork
		{
			get; set;
		}
		
		public int MinRPCVersion
		{
			get;
			internal set;
		}
		public string CryptoCode
		{
			get;
			set;
		}
		public NBXplorerDefaultSettings DefaultSettings
		{
			get;
			set;
		}

		public override string ToString()
		{
			return CryptoCode.ToString();
		}
	}
}
