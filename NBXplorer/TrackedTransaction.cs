using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;

namespace NBXplorer
{
	public partial class TrackedTransaction
	{
		class CoinOutpointEqualityComparer : IEqualityComparer<ICoin>
		{

			private static readonly CoinOutpointEqualityComparer _Instance = new CoinOutpointEqualityComparer();
			public static CoinOutpointEqualityComparer Instance
			{
				get
				{
					return _Instance;
				}
			}
			public bool Equals(ICoin x, ICoin y)
			{
				return x.Outpoint == y.Outpoint;
			}

			public int GetHashCode(ICoin obj)
			{
				return obj.Outpoint.GetHashCode();
			}
		}

		public TrackedSource TrackedSource { get; }

		public TrackedTransaction(TrackedTransactionKey key, TrackedSource trackedSource, IEnumerable<Coin> receivedCoins, Dictionary<Script, KeyPath> knownScriptMapping)
		{
			if (key == null)
				throw new ArgumentNullException(nameof(key));
			if (!key.IsPruned)
			{
				throw new ArgumentException("The key should be pruned", nameof(key));
			}
			if (trackedSource == null)
				throw new ArgumentNullException(nameof(trackedSource));
			TrackedSource = trackedSource;
			Key = key;
			if (knownScriptMapping != null)
				KnownKeyPathMapping = knownScriptMapping;
			if (receivedCoins != null)
				ReceivedCoins.AddRange(receivedCoins);
		}
		public TrackedTransaction(TrackedTransactionKey key, TrackedSource trackedSource, Transaction transaction, Dictionary<Script, KeyPath> knownScriptMapping)
		{
			if (key == null)
				throw new ArgumentNullException(nameof(key));
			if (transaction == null)
				throw new ArgumentNullException(nameof(transaction));
			if (knownScriptMapping == null)
				throw new ArgumentNullException(nameof(knownScriptMapping));
			if (trackedSource == null)
				throw new ArgumentNullException(nameof(trackedSource));
			if (key.IsPruned)
			{
				throw new ArgumentException("The key should not be pruned", nameof(key));
			}
			Key = key;
			TrackedSource = trackedSource;
			Transaction = transaction;
			transaction.PrecomputeHash(false, true);
			KnownKeyPathMapping = knownScriptMapping;

			KnownKeyPathMappingUpdated();
		}

		internal void KnownKeyPathMappingUpdated()
		{
			if (Transaction == null)
				return;
			var scriptPubKey = (TrackedSource as IDestination)?.ScriptPubKey;
			for (int i = 0; i < Transaction.Outputs.Count; i++)
			{
				var output = Transaction.Outputs[i];
				if (KnownKeyPathMapping.ContainsKey(output.ScriptPubKey) || scriptPubKey == output.ScriptPubKey)
					ReceivedCoins.Add(new Coin(new OutPoint(Key.TxId, i), output));
			}
			if (!Transaction.IsCoinBase)
				SpentOutpoints.AddRange(Transaction.Inputs.Select(input => input.PrevOut));
		}

		public Dictionary<Script, KeyPath> KnownKeyPathMapping { get; } = new Dictionary<Script, KeyPath>();
		public HashSet<ICoin> ReceivedCoins { get; protected set; } = new HashSet<ICoin>(CoinOutpointEqualityComparer.Instance);
		public HashSet<OutPoint> SpentOutpoints { get; } = new HashSet<OutPoint>();

		public Transaction Transaction
		{
			get; set;
		}

		public TrackedTransactionKey Key { get; set; }
		public uint256 BlockHash => Key.BlockHash;
		public uint256 TransactionHash => Key.TxId;

		public DateTimeOffset Inserted
		{
			get; set;
		}

		public DateTimeOffset FirstSeen
		{
			get; set;
		}
		//public bool IsCoinBase => Transaction?.IsCoinBase is true;

		bool _IsCoinBase;
		public bool IsCoinBase
		{
			get
			{
				return Transaction?.IsCoinBase is true || _IsCoinBase;
			}
			set
			{
				_IsCoinBase = value;
			}
		}

		public int? BlockIndex { get; set; }
		public long? BlockHeight { get; set; }
		public bool Immature { get; internal set; }

		public IEnumerable<MatchedOutput> GetReceivedOutputs()
		{
			return this.ReceivedCoins
							.Select(o => (Index: (int)o.Outpoint.N,
												   Output: o,
												   KeyPath: KnownKeyPathMapping.TryGet(o.TxOut.ScriptPubKey)))
							.Where(o => o.KeyPath != null || o.Output.TxOut.ScriptPubKey == (TrackedSource as IDestination)?.ScriptPubKey)
							.Select(o => new MatchedOutput()
							{
								Index = o.Index,
								Value = o.Output.Amount,
								KeyPath = o.KeyPath,
								ScriptPubKey = o.Output.TxOut.ScriptPubKey
							});
		}

		public virtual ITrackedTransactionSerializable CreateBitcoinSerializable()
		{
			return new TransactionMatchData(this);
		}

		Dictionary<OutPoint, int> inputsIndexes;
		public int IndexOfInput(OutPoint spent)
		{
			if (Transaction is null)
				throw new InvalidOperationException("IndexOfInput need access to the underlying transaction");
			if (inputsIndexes is null)
			{
				inputsIndexes = new Dictionary<OutPoint, int>(Transaction.Inputs.Count);
				int i = 0;
				foreach (var outpoint in Transaction.Inputs.Select(i => i.PrevOut))
				{
					inputsIndexes.Add(outpoint, i);
					i++;
				}
			}
			return inputsIndexes[spent];
		}
	}

	public class TrackedTransactionKey
	{
		public uint256 TxId { get; }
		public uint256 BlockHash { get; }

		public static TrackedTransactionKey Parse(ReadOnlySpan<byte> str)
		{
			return Parse(Encoding.UTF8.GetString(str));
		}
		public static TrackedTransactionKey Parse(string str)
		{
			str = str.Split('-').Last();
			var splitted = str.Split(':');

			var txStr = splitted[0];
			uint256 txHash = new uint256(txStr);

			var blockHashStr = splitted[1];
			uint256 blockHash = null;
			if (blockHashStr.Length != 0)
				blockHash = new uint256(blockHashStr);

			var pruned = false;
			if (splitted.Length > 2)
			{
				pruned = splitted[2] == "P";
			}
			return new TrackedTransactionKey(txHash, blockHash, pruned);
		}

		public TrackedTransactionKey(uint256 txId, uint256 blockHash, bool pruned)
		{
			TxId = txId;
			BlockHash = blockHash;
			IsPruned = pruned;
		}

		public bool IsPruned { get; set; }

		public override bool Equals(object obj)
		{
			TrackedTransactionKey item = obj as TrackedTransactionKey;
			if (item == null)
				return false;
			return ToString().Equals(item.ToString());
		}
		public static bool operator ==(TrackedTransactionKey a, TrackedTransactionKey b)
		{
			if (System.Object.ReferenceEquals(a, b))
				return true;
			if (((object)a == null) || ((object)b == null))
				return false;
			return a.IsPruned == b.IsPruned &&
				 a.TxId == b.TxId &&
				 a.BlockHash == b.BlockHash;
		}

		public static bool operator !=(TrackedTransactionKey a, TrackedTransactionKey b)
		{
			return !(a == b);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(IsPruned, TxId, BlockHash);
		}

		public override string ToString()
		{
			var prunedSuffix = IsPruned ? ":P" : string.Empty;
			return $"{TxId}:{BlockHash}{prunedSuffix}";
		}
	}
}
