using NBitcoin;
using System.Collections;
using System.Collections.Generic;

namespace NBXplorer
{
	internal class UTXOByOutpoint : IEnumerable<KeyValuePair<OutPoint, ICoin>>
	{
		Dictionary<OutPoint, ICoin> _Inner;

		public UTXOByOutpoint(UTXOByOutpoint other)
		{
			_Inner = new Dictionary<OutPoint, ICoin>(other._Inner);
		}
		public UTXOByOutpoint()
		{
			_Inner = new Dictionary<OutPoint, ICoin>();
		}

		internal bool ContainsKey(OutPoint outpoint)
		{
			return _Inner.ContainsKey(outpoint);
		}

		internal bool Remove(OutPoint prevOut)
		{
			return _Inner.Remove(prevOut);
		}
		
		internal bool TryAdd(OutPoint outpoint, ICoin coin)
		{
			return _Inner.TryAdd(outpoint, coin);
		}

		public IEnumerator<KeyValuePair<OutPoint, ICoin>> GetEnumerator()
		{
			return _Inner.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}
