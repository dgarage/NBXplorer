using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.DerivationStrategy
{
	public class DerivationStrategyFactory
	{

		private readonly Network _Network;
		public Network Network
		{
			get
			{
				return _Network;
			}
		}
		public DerivationStrategyFactory(Network network)
		{
			if(network == null)
				throw new ArgumentNullException(nameof(network));
			_Network = network;
		}
		public IDerivationStrategy Parse(string str)
		{
			var key = _Network.Parse<BitcoinExtPubKey>(str);
			return new DirectDerivationStrategy(key);
		}

		public string Serialize(IDerivationStrategy value)
		{
			if(value == null)
				return null;
			var direct = value as DirectDerivationStrategy;
			if(direct != null)
				return direct.Root.GetWif(Network).ToString();
			return null;
		}
	}
}
