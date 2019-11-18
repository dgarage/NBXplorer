using NBitcoin;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer
{
	internal class UTXOByOutpoint : IEnumerable<KeyValuePair<OutPoint, Coin>>
	{
		Dictionary<OutPoint, Coin> _Inner;

		public UTXOByOutpoint(UTXOByOutpoint other)
		{
			_Inner = new Dictionary<OutPoint, Coin>(other._Inner);
		}
		public UTXOByOutpoint()
		{
			_Inner = new Dictionary<OutPoint, Coin>();
		}

		internal bool ContainsKey(OutPoint outpoint)
		{
			return _Inner.ContainsKey(outpoint);
		}

		internal bool Remove(OutPoint prevOut)
		{
			return _Inner.Remove(prevOut);
		}
		
		internal bool TryAdd(OutPoint outpoint, Coin coin)
		{
			return _Inner.TryAdd(outpoint, coin);
		}

		public IEnumerator<KeyValuePair<OutPoint, Coin>> GetEnumerator()
		{
			return _Inner.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}
