using NBitcoin;
using System.Collections.Generic;
using System.Linq;

namespace NBXplorer
{
	public enum ApplyTransactionResult
	{
		Passed,
		Conflict
	}

	public class UTXOState
	{
		public UTXOState()
		{
			this.UTXOByOutpoint = new UTXOByOutpoint();
			this.SpentUTXOs = new HashSet<OutPoint>();
			this._KnownInputs = new HashSet<OutPoint>();
		}
		public UTXOState(UTXOState other)
		{
			UTXOByOutpoint = new UTXOByOutpoint(other.UTXOByOutpoint);
			SpentUTXOs = new HashSet<OutPoint>(other.SpentUTXOs);
			_KnownInputs = new HashSet<OutPoint>(other._KnownInputs);
		}
		public UTXOState(int txcount)
		{
			this.UTXOByOutpoint = new UTXOByOutpoint();
			this.SpentUTXOs = new HashSet<OutPoint>(txcount * 2);
			this._KnownInputs = new HashSet<OutPoint>(txcount * 2);
		}
		internal UTXOByOutpoint UTXOByOutpoint
		{
			get;
		}

		public HashSet<OutPoint> SpentUTXOs
		{
			get;
		}
		public ApplyTransactionResult Apply(TrackedTransaction trackedTransaction)
		{
			var result = ApplyTransactionResult.Passed;
			var hash = trackedTransaction.Key.TxId;

			foreach(var coin in trackedTransaction.ReceivedCoins)
			{
				if(UTXOByOutpoint.ContainsKey(coin.Outpoint))
				{
					result = ApplyTransactionResult.Conflict;
				}
			}

			foreach(var spentOutpoint in trackedTransaction.SpentOutpoints.Select(o => o.Outpoint))
			{
				if(_KnownInputs.Contains(spentOutpoint) || 
					(!UTXOByOutpoint.ContainsKey(spentOutpoint) && SpentUTXOs.Contains(spentOutpoint)))
				{
					result = ApplyTransactionResult.Conflict;
				}
			}
			if(result == ApplyTransactionResult.Conflict)
				return result;

			foreach(var coin in trackedTransaction.ReceivedCoins)
			{
				UTXOByOutpoint.TryAdd(coin.Outpoint, coin);
			}

			foreach (var spentOutpoint in trackedTransaction.SpentOutpoints.Select(o => o.Outpoint))
			{
				if(UTXOByOutpoint.Remove(spentOutpoint))
				{
					SpentUTXOs.Add(spentOutpoint);
				}
				_KnownInputs.Add(spentOutpoint);
			}
			return result;
		}
		readonly HashSet<OutPoint> _KnownInputs;

		public UTXOState Snapshot()
		{
			return new UTXOState(this);
		}

		public static UTXOState operator-(UTXOState a, UTXOState b)
		{
			UTXOState result = new UTXOState();
			foreach (var utxo in a.UTXOByOutpoint)
			{
				if (!b.UTXOByOutpoint.ContainsKey(utxo.Key))
					result.UTXOByOutpoint.TryAdd(utxo.Key, utxo.Value);
			}
			foreach (var utxo in b.UTXOByOutpoint)
			{
				if (!a.UTXOByOutpoint.ContainsKey(utxo.Key))
					result.SpentUTXOs.Add(utxo.Key);
			}
			return result;
		}
	}
}
