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
	public class UTXOEvent
	{
		public uint256 TxId
		{
			get; set;
		}
		public bool Received
		{
			get; set;
		}
		public OutPoint Outpoint
		{
			get; set;
		}
	}

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
					Conflicts.Add(coin.Outpoint, hash);
				}
			}

			foreach(var spentOutpoint in trackedTransaction.SpentOutpoints)
			{
				if(_KnownInputs.Contains(spentOutpoint) || 
					(!UTXOByOutpoint.ContainsKey(spentOutpoint) && SpentUTXOs.Contains(spentOutpoint)))
				{
					result = ApplyTransactionResult.Conflict;
					Conflicts.Add(spentOutpoint, hash);
				}
			}
			if(result == ApplyTransactionResult.Conflict)
				return result;

			_TransactionTimes.Add(trackedTransaction.FirstSeen);

			foreach(var coin in trackedTransaction.ReceivedCoins)
			{
				if(UTXOByOutpoint.TryAdd(coin.Outpoint, coin))
				{
					AddEvent(new UTXOEvent() { Received = true, Outpoint = coin.Outpoint, TxId = hash });
				}
			}

			if (trackedTransaction.ReceivedCoins.Count == 0 && trackedTransaction.Transaction != null)
				UTXOByOutpoint.Prunable.Add(new Prunable() { PrunedBy = hash, TransactionId = hash });

			foreach (var spentOutpoint in trackedTransaction.SpentOutpoints)
			{
				if(UTXOByOutpoint.Remove(spentOutpoint, hash))
				{
					AddEvent(new UTXOEvent() { Received = false, Outpoint = spentOutpoint, TxId = hash });
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

		public MultiValueDictionary<OutPoint, uint256> Conflicts
		{
			get; set;
		} = new MultiValueDictionary<OutPoint, uint256>();

		

		BookmarkProcessor _BookmarkProcessor = new BookmarkProcessor(32 + 32 + 32 + 4 + 1);

		public Bookmark CurrentBookmark
		{
			get
			{
				return _BookmarkProcessor.CurrentBookmark;
			}
		}


		private void AddEvent(UTXOEvent evt)
		{
			_BookmarkProcessor.PushNew();
			_BookmarkProcessor.AddData(evt.TxId.ToBytes());
			_BookmarkProcessor.AddData(evt.Outpoint);
			_BookmarkProcessor.AddData(evt.Received);
			_BookmarkProcessor.UpdateBookmark();
		}

		public UTXOState Snapshot()
		{
			return new UTXOState()
			{
				UTXOByOutpoint = new UTXOByOutpoint(UTXOByOutpoint),
				Conflicts = new MultiValueDictionary<OutPoint, uint256>(Conflicts),
				SpentUTXOs = new HashSet<OutPoint>(SpentUTXOs),
				_BookmarkProcessor = _BookmarkProcessor.Clone(),
				_KnownInputs = new HashSet<OutPoint>(_KnownInputs),
				_TransactionTimes = new List<DateTimeOffset>(_TransactionTimes)
			};
		}
		

		internal void ResetEvents()
		{
			_BookmarkProcessor.Clear();
		}
	}
}
