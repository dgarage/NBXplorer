using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer
{
	public enum ChainType
	{
		Regtest,
		Main,
		Test
	}

	public static class ChainTypeExtensions
	{
		public static ChainType ToChainType(this Network network)
		{
			if(network == Network.Main)
				return ChainType.Main;
			if(network == Network.RegTest)
				return ChainType.Regtest;
			if(network == Network.TestNet)
				return ChainType.Regtest;
			throw new NotSupportedException(network.Name);
		}
		public static Network ToNetwork(this ChainType chainType)
		{
			if(chainType == ChainType.Main)
				return Network.Main;
			if(chainType == ChainType.Test)
				return Network.TestNet;
			if(chainType == ChainType.Regtest)
				return Network.RegTest;
			throw new NotSupportedException(chainType.ToString());
		}
	}
}
