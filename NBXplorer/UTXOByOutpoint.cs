using NBitcoin;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer
{
	internal class Prunable
	{
		public uint256 PrunedBy { get; set; }
		public uint256 TransactionId { get; set; }
	}
	internal class UTXOByOutpoint : IEnumerable<KeyValuePair<OutPoint, Coin>>
	{
		Dictionary<OutPoint, Coin> _Inner;
		Dictionary<uint256, int> _AvailableOutputs = new Dictionary<uint256, int>();
		public List<Prunable> Prunable { get; } = new List<Prunable>();

		public UTXOByOutpoint(UTXOByOutpoint other)
		{
			_Inner = new Dictionary<OutPoint, Coin>(other._Inner);
			_AvailableOutputs = new Dictionary<uint256, int>(other._AvailableOutputs);
			Prunable = new List<Prunable>(other.Prunable);
		}
		public UTXOByOutpoint()
		{
			_Inner = new Dictionary<OutPoint, Coin>();
		}

		internal bool ContainsKey(OutPoint outpoint)
		{
			return _Inner.ContainsKey(outpoint);
		}

		internal bool Remove(OutPoint prevOut, uint256 removedBy)
		{
			if(_Inner.Remove(prevOut))
			{
				var count = _AvailableOutputs[prevOut.Hash];
				if(count == 1)
				{
					Prunable.Add(new NBXplorer.Prunable() { PrunedBy = removedBy, TransactionId = prevOut.Hash });
					_AvailableOutputs.Remove(prevOut.Hash);
				}
				else
				{
					_AvailableOutputs[prevOut.Hash] = count - 1;
				}
				return true;
			}
			return false;
		}
		
		internal bool TryAdd(OutPoint outpoint, Coin coin)
		{
			if(_Inner.TryAdd(outpoint, coin))
			{
				if(_AvailableOutputs.TryGetValue(outpoint.Hash, out int count))
				{
					_AvailableOutputs[outpoint.Hash] = count + 1;
				}
				else
				{
					_AvailableOutputs.Add(outpoint.Hash, 1);
				}
				return true;
			}
			return false;
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
