using NBitcoin;
using NBXplorer.Models;
using System.Collections;
using System.Collections.Generic;

namespace NBXplorer
{
	internal class UTXOByOutpoint : IEnumerable<KeyValuePair<OutPoint, MatchedOutput>>
	{
		Dictionary<OutPoint, MatchedOutput> _Inner;

		public UTXOByOutpoint(UTXOByOutpoint other)
		{
			_Inner = new Dictionary<OutPoint, MatchedOutput>(other._Inner);
		}
		public UTXOByOutpoint()
		{
			_Inner = new Dictionary<OutPoint, MatchedOutput>();
		}

		internal bool ContainsKey(OutPoint outpoint)
		{
			return _Inner.ContainsKey(outpoint);
		}

		internal bool Remove(OutPoint prevOut)
		{
			return _Inner.Remove(prevOut);
		}
		
		internal bool TryAdd(OutPoint outpoint, MatchedOutput coin)
		{
			return _Inner.TryAdd(outpoint, coin);
		}

		public IEnumerator<KeyValuePair<OutPoint, MatchedOutput>> GetEnumerator()
		{
			return _Inner.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}
