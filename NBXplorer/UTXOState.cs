using NBitcoin;
using System.Linq;
using NBitcoin.Crypto;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NBXplorer
{
	public class UTXOEvent
	{
		public uint256 TxId
		{
			get; set;
		}
		public bool Added
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
		Conflict,
		Success
	}

	public class UTXOState
	{
		byte[] _Buffer;
		public UTXOState()
		{
			_Buffer = new byte[32 + 32 + 32 + 4];
			_Hasher = new MemoryStream(_Buffer);
		}

		public Dictionary<OutPoint, Coin> CoinsByOutpoint
		{
			get; set;
		} = new Dictionary<OutPoint, Coin>();

		public Func<Script, bool> MatchScript
		{
			get; set;
		}

		public List<UTXOEvent> Events
		{
			get; set;
		} = new List<UTXOEvent>();

		public HashSet<OutPoint> SpentOutpoints
		{
			get; set;
		} = new HashSet<OutPoint>();

		public ApplyTransactionResult Apply(Transaction tx)
		{
			var result = ApplyTransactionResult.Passed;
			var hash = tx.GetHash();

			for(int i = 0; i < tx.Outputs.Count; i++)
			{
				var output = tx.Outputs[i];
				var outpoint = new OutPoint(hash, i);
				if(CoinsByOutpoint.ContainsKey(outpoint))
				{
					result = ApplyTransactionResult.Conflict;
					Conflicts.Add(outpoint, hash);
				}
			}

			for(int i = 0; i < tx.Inputs.Count; i++)
			{
				var input = tx.Inputs[i];
				if(!CoinsByOutpoint.ContainsKey(input.PrevOut) && SpentOutpoints.Contains(input.PrevOut))
				{
					result = ApplyTransactionResult.Conflict;
					Conflicts.Add(input.PrevOut, hash);
				}
			}
			if(result == ApplyTransactionResult.Conflict)
				return result;

			for(int i = 0; i < tx.Outputs.Count; i++)
			{
				var output = tx.Outputs[i];
				if(MatchScript(output.ScriptPubKey))
				{
					var outpoint = new OutPoint(hash, i);
					if(CoinsByOutpoint.TryAdd(outpoint, new Coin(outpoint, output)))
					{
						Events.Add(new UTXOEvent() { Added = true, Outpoint = outpoint, TxId = hash });
						UpdateHash(hash, outpoint);
					}
				}
			}

			for(int i = 0; i < tx.Inputs.Count; i++)
			{
				var input = tx.Inputs[i];
				if(CoinsByOutpoint.Remove(input.PrevOut))
				{
					Events.Add(new UTXOEvent() { Added = false, Outpoint = input.PrevOut, TxId = hash });
					SpentOutpoints.Add(input.PrevOut);
					UpdateHash(hash, input.PrevOut);
				}
			}
			return result;
		}

		public MultiValueDictionary<OutPoint, uint256> Conflicts
		{
			get; set;
		} = new MultiValueDictionary<OutPoint, uint256>();

		MemoryStream _Hasher = new MemoryStream();
		byte[] _CurrentHash = new byte[32];

		public uint256 CurrentHash
		{
			get
			{
				return new uint256(_CurrentHash);
			}
		}

		void UpdateHash(uint256 hash, OutPoint outpoint)
		{
			_Hasher.Position = 0;
			_Hasher.Write(_CurrentHash, 0, 32);
			_Hasher.Write(hash.ToBytes(), 0, 32);
			var bs = new BitcoinStream(_Hasher, true);
			bs.ReadWrite(ref outpoint);
			_CurrentHash = Hashes.SHA256(_Buffer);
		}

		public UTXOState Snapshot()
		{
			var buffer = _Buffer.ToArray();
			return new UTXOState()
			{
				CoinsByOutpoint = new Dictionary<OutPoint, Coin>(CoinsByOutpoint),
				Conflicts = new MultiValueDictionary<OutPoint, uint256>(Conflicts),
				Events = new List<UTXOEvent>(Events),
				MatchScript = MatchScript,
				SpentOutpoints = new HashSet<OutPoint>(SpentOutpoints),
				_Buffer = buffer.ToArray(),
				_CurrentHash = _CurrentHash.ToArray(),
				_Hasher = new MemoryStream(buffer)
			};
		}
		

		internal void ResetEvents()
		{
			Array.Clear(_CurrentHash, 0, _CurrentHash.Length);
			Events.Clear();
		}

		public void Remove(UTXOState otherState)
		{
			foreach(var spent in otherState.SpentOutpoints)
				this.SpentOutpoints.Remove(spent);
			foreach(var spent in otherState.CoinsByOutpoint)
				this.CoinsByOutpoint.Remove(spent.Key);
		}
	}
}
