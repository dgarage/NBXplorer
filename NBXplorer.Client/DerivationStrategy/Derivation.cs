using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.DerivationStrategy
{
	public class Derivation
	{
		public Derivation()
		{

		}
		public Script ScriptPubKey
		{
			get; set;
		}
		public Script Redeem
		{
			get; set;
		}

		public virtual BitcoinAddress GetAddress(Network network)
		{
			return ScriptPubKey.GetDestinationAddress(network);
		}
	}
}
