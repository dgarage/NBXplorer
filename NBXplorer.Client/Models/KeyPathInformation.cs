using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
    public class KeyPathInformation
    {
		public KeyPath KeyPath
		{
			get; set;
		}
		public Script ScriptPubKey
		{
			get; set;
		}
		public Script Redeem
		{
			get; set;
		}
		public BitcoinAddress Address
		{
			get; set;
		}
	}
}
