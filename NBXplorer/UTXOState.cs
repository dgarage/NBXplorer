using NBitcoin;
using System.Linq;
using NBitcoin.Crypto;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NBXplorer.Models;

namespace NBXplorer
{
	public enum ApplyTransactionResult
	{
		Passed,
		Conflict
	}

	public class UTXOState
	{

		internal UTXOByOutpoint UTXOByOutpoint
		{
			get; set;
		} = new UTXOByOutpoint();

		public Func<Script[], bool[]> MatchScript
		{
			get; set;
		}

		public HashSet<OutPoint> SpentUTXOs
		{
			get; set;
		} = new HashSet<OutPoint>();
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

			foreach(var spentOutpoint in trackedTransaction.SpentOutpoints)
			{
				if(_KnownInputs.Contains(spentOutpoint) || 
					(!UTXOByOutpoint.ContainsKey(spentOutpoint) && SpentUTXOs.Contains(spentOutpoint)))
				{
					result = ApplyTransactionResult.Conflict;
				}
			}
			if(result == ApplyTransactionResult.Conflict)
				return result;

			_TransactionTimes.Add(trackedTransaction.FirstSeen);

			foreach(var coin in trackedTransaction.ReceivedCoins)
			{
				UTXOByOutpoint.TryAdd(coin.Outpoint, coin);
			}

			if (trackedTransaction.ReceivedCoins.Count == 0 && trackedTransaction.Transaction != null)
				UTXOByOutpoint.Prunable.Add(new Prunable() { PrunedBy = hash, TransactionId = hash });

			foreach (var spentOutpoint in trackedTransaction.SpentOutpoints)
			{
				if(UTXOByOutpoint.Remove(spentOutpoint, hash))
				{
					SpentUTXOs.Add(spentOutpoint);
				}
				_KnownInputs.Add(spentOutpoint);
			}
			return result;
		}
		HashSet<OutPoint> _KnownInputs = new HashSet<OutPoint>();
		List<DateTimeOffset> _TransactionTimes = new List<DateTimeOffset>();
		public DateTimeOffset? GetQuarterTransactionTime()
		{
			var times = _TransactionTimes.ToArray();
			Array.Sort(times);
			var quarter = times.Length / 4;
			if (times.Length <= quarter)
				return null;
			return times[quarter];
		}

		public UTXOState Snapshot()
		{
			return new UTXOState()
			{
				UTXOByOutpoint = new UTXOByOutpoint(UTXOByOutpoint),
				SpentUTXOs = new HashSet<OutPoint>(SpentUTXOs),
				_KnownInputs = new HashSet<OutPoint>(_KnownInputs),
				_TransactionTimes = new List<DateTimeOffset>(_TransactionTimes)
			};
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
