using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NBXplorer
{
	public partial class TrackedTransaction
	{
		internal class TransactionMiniMatch : IBitcoinSerializable
		{

			public TransactionMiniMatch()
			{
				_Outputs = Array.Empty<TransactionMiniKeyInformation>();
				_Inputs = Array.Empty<TransactionMiniKeyInformation>();
			}

			TransactionMiniKeyInformation[] _Outputs;
			public TransactionMiniKeyInformation[] Outputs
			{
				get
				{
					return _Outputs;
				}
				set
				{
					_Outputs = value;
				}
			}


			TransactionMiniKeyInformation[] _Inputs;
			public TransactionMiniKeyInformation[] Inputs
			{
				get
				{
					return _Inputs;
				}
				set
				{
					_Inputs = value;
				}
			}

			public void ReadWrite(BitcoinStream stream)
			{
				stream.ReadWrite(ref _Inputs);
				stream.ReadWrite(ref _Outputs);
			}
		}
		internal class TransactionMatchData : ITrackedTransactionSerializable
		{
			class CoinData : IBitcoinSerializable
			{
				public CoinData()
				{

				}
				public CoinData(uint index, TxOut txOut)
				{
					_Index = index;
					_TxOut = txOut;
				}
				private uint _Index;
				public uint Index
				{
					get
					{
						return _Index;
					}
				}
				private TxOut _TxOut;
				public TxOut TxOut
				{
					get
					{
						return _TxOut;
					}
				}

				public void ReadWrite(BitcoinStream stream)
				{
					stream.ReadWriteAsVarInt(ref _Index);
					stream.ReadWrite(ref _TxOut);
				}
			}
			public TransactionMatchData(TrackedTransactionKey key)
			{
				if (key == null)
					throw new ArgumentNullException(nameof(key));
				Key = key;
			}
			public TransactionMatchData(TrackedTransaction trackedTransaction)
			{
				if (trackedTransaction == null)
					throw new ArgumentNullException(nameof(trackedTransaction));
				Key = trackedTransaction.Key;
				Transaction = trackedTransaction.Transaction;
				FirstSeenTickCount = trackedTransaction.FirstSeen.Ticks;
				TickCount = trackedTransaction.Inserted.Ticks;
				KnownKeyPathMapping = trackedTransaction.KnownKeyPathMapping;
				if (trackedTransaction.Key.IsPruned)
				{
					_CoinsData = trackedTransaction.ReceivedCoins.Select(c => new CoinData(c.Outpoint.N, c.TxOut)).ToArray();
				}
			}
			public TrackedTransactionKey Key { get; }
			Transaction _Transaction;
			public Transaction Transaction
			{
				get
				{
					return _Transaction;
				}
				set
				{
					_Transaction = value;
				}
			}


			CoinData[] _CoinsData;
			CoinData[] CoinsData
			{
				get
				{
					return _CoinsData;
				}
				set
				{
					_CoinsData = value;
				}
			}


			long _TickCount;
			public long TickCount
			{
				get
				{
					return _TickCount;
				}
				set
				{
					_TickCount = value;
				}
			}

			public Dictionary<Script, KeyPath> KnownKeyPathMapping { get; set; }

			long _FirstSeenTickCount;
			public long FirstSeenTickCount
			{
				get
				{
					return _FirstSeenTickCount;
				}
				set
				{
					_FirstSeenTickCount = value;
				}
			}

			public virtual void ReadWrite(BitcoinStream stream)
			{
				if (Key.IsPruned)
				{
					stream.ReadWrite(ref _CoinsData);
				}
				else
				{
					stream.ReadWrite(ref _Transaction);
				}
				stream.ReadWrite(ref _TickCount);
				if (stream.Serializing)
				{
					var match = new TransactionMiniMatch();
					match.Outputs = KnownKeyPathMapping.Select(kv => new TransactionMiniKeyInformation() { ScriptPubKey = kv.Key, KeyPath = kv.Value }).ToArray();
					stream.ReadWrite(ref match);
				}
				else
				{
					var match = new TransactionMiniMatch();
					stream.ReadWrite(ref match);
					KnownKeyPathMapping = new Dictionary<Script, KeyPath>();
					foreach (var kv in match.Inputs.Concat(match.Outputs))
					{
						KnownKeyPathMapping.TryAdd(kv.ScriptPubKey, kv.KeyPath);
					}
				}
				stream.ReadWrite(ref _FirstSeenTickCount);
			}

			public virtual IEnumerable<Coin> GetCoins()
			{
				if (_CoinsData is null)
				{
					int idx = -1;
					foreach (var output in Transaction.Outputs)
					{
						idx++;
						if (KnownKeyPathMapping.ContainsKey(output.ScriptPubKey))
							yield return new Coin(new OutPoint(Key.TxId, idx), output);
					}
				}
				else
				{
					foreach (var coinData in _CoinsData)
					{
						yield return new Coin(new OutPoint(Key.TxId, (int)coinData.Index), coinData.TxOut);
					}
				}
			}
		}

		internal class TransactionMiniKeyInformation : IBitcoinSerializable
		{
			public TransactionMiniKeyInformation()
			{

			}

			Script _ScriptPubKey;
			public Script ScriptPubKey
			{
				get
				{
					return _ScriptPubKey;
				}
				set
				{
					_ScriptPubKey = value;
				}
			}

			KeyPath _KeyPath;
			public KeyPath KeyPath
			{
				get
				{
					return _KeyPath;
				}
				set
				{
					_KeyPath = value;
				}
			}

			public virtual void ReadWrite(BitcoinStream stream)
			{
				stream.ReadWrite(ref _ScriptPubKey);
				if (stream.Serializing)
				{
					if (_KeyPath == null)
					{
						stream.ReadWrite((byte)0);
					}
					else
					{
						stream.ReadWrite((byte)_KeyPath.Indexes.Length);
						foreach (var index in _KeyPath.Indexes)
						{
							stream.ReadWrite(index);
						}
					}
				}
				else
				{
					byte len = 0;
					stream.ReadWrite(ref len);
					var indexes = new uint[len];
					for (int i = 0; i < len; i++)
					{
						uint index = 0;
						stream.ReadWrite(ref index);
						indexes[i] = index;
					}
					if (len != 0)
						_KeyPath = new KeyPath(indexes);
				}
			}
		}
	}

	public interface ITrackedTransactionSerializable : IBitcoinSerializable
	{
		TrackedTransactionKey Key { get; }
		IEnumerable<Coin> GetCoins();
		long FirstSeenTickCount { get; set; }
		long TickCount { get;  }
		Transaction Transaction { get; }
		Dictionary<Script, KeyPath> KnownKeyPathMapping { get; }
	}
}
