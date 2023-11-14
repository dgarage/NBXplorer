using System;
using System.Linq;
using System.Collections.Generic;
using NBitcoin;
using NBXplorer.Models;

namespace NBXplorer.Tests
{
	public class TrackedTransactionBuilder
	{
		internal TrackedSource _TrackedSource = new AddressTrackedSource(new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.RegTest));
		internal List<TransactionContext> _Transactions = new List<TransactionContext>();
		public class TransactionContext
		{
			uint256 _BlockId;
			internal uint256 _TransactionId;
			TrackedTransactionBuilder _Parent;
			internal DateTimeOffset _TimeStamp;
			List<OutputContext> _Outputs = new List<OutputContext>();
			List<OutputContext> _Inputs = new List<OutputContext>();

			public TrackedTransaction Build()
			{
				var tx = new TrackedTransaction(new TrackedTransactionKey(_TransactionId, _BlockId, true), _Parent._TrackedSource, null as Coin[], null)
				{
					Inserted = _TimeStamp,
					FirstSeen = _TimeStamp
				};
				foreach (var input in _Inputs)
				{
					tx.SpentOutpoints.Add(input.Coin.Outpoint);
				}
				foreach (var output in _Outputs)
				{
					tx.ReceivedCoins.Add(output.Coin);
				}
				return tx;
			}

			public TransactionContext(TrackedTransactionBuilder parent)
			{
				_TransactionId = NBitcoin.RandomUtils.GetUInt256();
				_Parent = parent;
			}

			public TransactionContext(TransactionContext source)
			{
				this._BlockId = source._BlockId;
				this._Inputs = source._Inputs.ToList();
				this._Outputs = source._Outputs.ToList();
				this._Parent = source._Parent;
				this._TimeStamp = source._TimeStamp;
				this._TransactionId = source._TransactionId;
			}

			public TransactionContext AddOutput(out OutputContext output)
			{
				output = new OutputContext(this, _Outputs.Count);
				_Outputs.Add(output);
				return this;
			}

			public TransactionContext Spend(OutputContext output)
			{
				_Inputs.Add(output);
				return this;
			}

			public TransactionContext Timestamp(long time)
			{
				_TimeStamp = NBitcoin.Utils.UnixTimeToDateTime(time);
				return this;
			}

			public TransactionContext MinedBy(uint256 blockId)
			{
				_BlockId = blockId;
				return this;
			}
		}
		public class OutputContext
		{
			TransactionContext _Parent;
			public OutputContext(TransactionContext parent, int i)
			{
				_Parent = parent;
				Coin = new Coin(new OutPoint(_Parent._TransactionId, i), new TxOut(Money.Zero, Script.Empty));
			}

			public Coin Coin { get; internal set; }
		}
		public TrackedTransactionBuilder()
		{

		}

		internal TransactionContext CreateTransaction(out TransactionContext tx)
		{
			tx = new TransactionContext(this);
			_Transactions.Add(tx);
			return tx;
		}

		public TrackedTransaction[] Build()
		{
			return _Transactions.Select(t => t.Build()).ToArray();
		}

		internal TransactionContext Dup(TransactionContext source, out TransactionContext duplicate)
		{
			duplicate = new TransactionContext(source);
			_Transactions.Add(duplicate);
			return duplicate;
		}
	}
}
