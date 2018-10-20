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

		public Dictionary<OutPoint, Coin> UTXOByOutpoint
		{
			get; set;
		} = new Dictionary<OutPoint, Coin>();

		public Func<Script[], bool[]> MatchScript
		{
			get; set;
		}

		public List<UTXOEvent> Events
		{
			get; set;
		} = new List<UTXOEvent>();

		public HashSet<OutPoint> SpentUTXOs
		{
			get; set;
		} = new HashSet<OutPoint>();
		public ApplyTransactionResult Apply(TrackedTransaction fullTrackedTransaction)
		{
			var tx = fullTrackedTransaction.Transaction;
			var result = ApplyTransactionResult.Passed;
			var hash = fullTrackedTransaction.Key.TxId;

			for(int i = 0; i < tx.Outputs.Count; i++)
			{
				var output = tx.Outputs[i];
				var outpoint = new OutPoint(hash, i);
				if(UTXOByOutpoint.ContainsKey(outpoint))
				{
					result = ApplyTransactionResult.Conflict;
					Conflicts.Add(outpoint, hash);
				}
			}

			for(int i = 0; i < tx.Inputs.Count; i++)
			{
				var input = tx.Inputs[i];
				if(_KnownInputs.Contains(input.PrevOut) || 
					(!UTXOByOutpoint.ContainsKey(input.PrevOut) && SpentUTXOs.Contains(input.PrevOut)))
				{
					result = ApplyTransactionResult.Conflict;
					Conflicts.Add(input.PrevOut, hash);
				}
			}
			if(result == ApplyTransactionResult.Conflict)
				return result;

			var matches = MatchScript == null ? null : MatchScript(tx.Outputs.Select(o => o.ScriptPubKey).ToArray());
			for(int i = 0; i < tx.Outputs.Count; i++)
			{
				var output = tx.Outputs[i];
				var matched = matches == null ? true : matches[i];
				if(matched)
				{
					var outpoint = new OutPoint(hash, i);
					if(UTXOByOutpoint.TryAdd(outpoint, new Coin(outpoint, output)))
					{
						AddEvent(new UTXOEvent() { Received = true, Outpoint = outpoint, TxId = hash });
					}
				}
			}

			for(int i = 0; i < tx.Inputs.Count; i++)
			{
				var input = tx.Inputs[i];
				if(UTXOByOutpoint.Remove(input.PrevOut))
				{
					AddEvent(new UTXOEvent() { Received = false, Outpoint = input.PrevOut, TxId = hash });
					SpentUTXOs.Add(input.PrevOut);
				}
				_KnownInputs.Add(input.PrevOut);
			}
			return result;
		}

		HashSet<OutPoint> _KnownInputs = new HashSet<OutPoint>();

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
			Events.Add(evt);
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
				UTXOByOutpoint = new Dictionary<OutPoint, Coin>(UTXOByOutpoint),
				Conflicts = new MultiValueDictionary<OutPoint, uint256>(Conflicts),
				Events = new List<UTXOEvent>(Events),
				MatchScript = MatchScript,
				SpentUTXOs = new HashSet<OutPoint>(SpentUTXOs),
				_BookmarkProcessor = _BookmarkProcessor.Clone()
			};
		}
		

		internal void ResetEvents()
		{
			_BookmarkProcessor.Clear();
			Events.Clear();
		}
	}
}
