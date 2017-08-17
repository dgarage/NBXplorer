using NBitcoin;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin.DataEncoders;

namespace ElementsExplorer
{
	public class NamedIssuance
	{
		public string Name
		{
			get;
			set;
		}
		public uint256 AssetId
		{
			get;
			set;
		}
	}
}
