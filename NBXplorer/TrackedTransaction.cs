using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NBitcoin;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;
using static NBXplorer.Backend.DbConnectionHelper;

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

#nullable enable
		public static TrackedTransaction Create(TrackedSource trackedSource, SaveTransactionRecord record)
		{
			var tracked = record switch
			{
				{ Transaction: { } tx } => new TrackedTransaction(
							new TrackedTransactionKey(record.Id, record.BlockId, false),
							trackedSource,
							tx),
				_ => new TrackedTransaction(
							new TrackedTransactionKey(record.Id, record.BlockId, true),
							trackedSource)
			};
			tracked.BlockHeight = record.BlockHeight;
			tracked.FirstSeen = record.SeenAt;
			tracked.BlockIndex = record.BlockIndex;
			tracked.Immature = record.Immature;
			return tracked;
		}
#nullable restore
		TrackedTransaction(TrackedTransactionKey key, TrackedSource trackedSource)
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
		}
		TrackedTransaction(TrackedTransactionKey key, TrackedSource trackedSource, Transaction transaction)
		{
			if (key == null)
				throw new ArgumentNullException(nameof(key));
			if (transaction == null)
				throw new ArgumentNullException(nameof(transaction));
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
			if (!Transaction.IsCoinBase)
				SpentOutpoints.AddInputs(Transaction);
		}

		public class SpentOutpointsSet : HashSet<(OutPoint Outpoint, int InputIndex)>
		{
			public void AddInputs(Transaction tx)
			{
				foreach (var asIndexedInput in tx.Inputs.AsIndexedInputs())
				{
					Add((asIndexedInput.PrevOut, (int)asIndexedInput.Index));
				}
			}
			public void Add(OutPoint outpoint, int inputIndex) => Add((outpoint, inputIndex));
		}
		public SpentOutpointsSet SpentOutpoints { get; } = new();

		public Transaction Transaction
		{
			get; set;
		}

		public TrackedTransactionKey Key { get; set; }
		public uint256 BlockHash => Key.BlockHash;
		public uint256 TransactionHash => Key.TxId;

		public DateTimeOffset FirstSeen
		{
			get; set;
		}

		public int? BlockIndex { get; set; }
		public long? BlockHeight { get; set; }
		public bool Immature { get; internal set; }
		public HashSet<uint256> Replacing { get; internal set; }
		public List<MatchedInput> MatchedInputs { get; private set; } = new List<MatchedInput>();
		public List<MatchedOutput> MatchedOutputs { get; private set; } = new List<MatchedOutput>();

		internal void AddKnownKeyPathInformation(MultiValueDictionary<Script, KeyPathInformation> keyPathInfoByScript)
		{
			foreach (var m in InOuts)
			{
				if (keyPathInfoByScript.TryGetValue(m.ScriptPubKey, out var keyPathInfos))
				{
					foreach (var keyPathInfo in keyPathInfos)
					{
						if (keyPathInfo.TrackedSource != TrackedSource)
							continue;
						m.KeyPath ??= keyPathInfo.KeyPath;
						// No "??=" is intentional, we want to override the address for liquid
						m.Address = keyPathInfo.Address;
					}
				}
			}
		}

		internal void Sort()
		{
			MatchedInputs.Sort((a,b) => a.InputIndex.CompareTo(b.InputIndex));
			MatchedOutputs.Sort((a, b) => a.Index.CompareTo(b.Index));
		}

		public IEnumerable<MatchedOutput> InOuts => MatchedInputs.Concat(MatchedOutputs);

		public TransactionMetadata Metadata { get; set; }
	}


	public record TrackedTransactionKey(uint256 TxId, uint256 BlockHash, bool IsPruned);
}
