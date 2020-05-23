using NBitcoin;
using NBitcoin.Crypto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace NBXplorer.Analytics
{
	public class FingerprintDistribution
	{
		public static FingerprintDistribution Calculate(Block block)
		{
			if (block == null)
				throw new ArgumentNullException(nameof(block));
			return new FingerprintDistribution(block);
		}

		public static FingerprintDistribution operator+(FingerprintDistribution a, FingerprintDistribution b)
		{
			if (a == null)
				return b;
			if (b == null)
				return a;
			var featureCounts = new Dictionary<Fingerprint, int>(a._FingerprintsCount);
			foreach (var f in b._FingerprintsCount)
			{
				if (featureCounts.TryGetValue(f.Key, out var v))
				{
					featureCounts[f.Key] = v + f.Value;
				}
				else
				{
					featureCounts.Add(f.Key, f.Value);
				}
			}
			return new FingerprintDistribution(featureCounts);
		}
		public static FingerprintDistribution operator -(FingerprintDistribution a, FingerprintDistribution b)
		{
			if (a == null)
				throw new ArgumentNullException(nameof(a));
			if (b == null)
				return a;
			var featureCounts = new Dictionary<Fingerprint, int>(a._FingerprintsCount);
			foreach (var f in b._FingerprintsCount)
			{
				if (featureCounts.TryGetValue(f.Key, out var v))
				{
					var newValue = v - f.Value;
					featureCounts[f.Key] = newValue;
					if (newValue < 0)
						throw new InvalidOperationException("Substracting would result in negative counts");
				}
				else
				{
					throw new InvalidOperationException("Substracting would result in negative counts");
				}
			}
			return new FingerprintDistribution(featureCounts);
		}

		Dictionary<Fingerprint, int> _FingerprintsCount;
		public IReadOnlyDictionary<Fingerprint, int> RawDistribution
		{
			get
			{
				return _FingerprintsCount;
			}
		}

		public int GetFingerprintCount(Fingerprint fingerprint)
		{
			_FingerprintsCount.TryGetValue(fingerprint, out var v);
			return v;
		}

		public int TotalCount { get; }
		FingerprintDistribution(Block block)
		{
			if (block == null)
				throw new ArgumentNullException(nameof(block));
			List<Fingerprint> features = new List<Fingerprint>(block.Transactions.Count);
			foreach (var tx in block.Transactions)
				features.Add(CalculateFingerprint(tx));
			_FingerprintsCount = features.GroupBy(f => f)
				.ToDictionary(f => f.First(), f => f.Count());
			TotalCount = _FingerprintsCount.Select(e => e.Value).Sum();
			if (TotalCount == 0)
				throw new InvalidOperationException("The block should have at least one transaction");
		}
		public FingerprintDistribution(Dictionary<Fingerprint, int> fingerprintCounts)
		{
			if (fingerprintCounts == null)
				throw new ArgumentNullException(nameof(fingerprintCounts));
			_FingerprintsCount = fingerprintCounts;
			TotalCount = _FingerprintsCount.Select(e => e.Value).Sum();
			if (TotalCount == 0)
				throw new InvalidOperationException("The dictionary should have at least one transaction");
		}

		/// <summary>
		/// Restrict the distribution with some set conditions
		/// </summary>
		/// <param name="conditions"></param>
		/// <returns></returns>
		/// <exception cref="System.InvalidOperationException">This is thrown if no fingerprint match the conditions, so we can't build a propre distribution</exception>
		public FingerprintDistribution KnowingThat(params (Fingerprint feature, bool value)[] conditions)
		{
			ulong mask = 0UL;
			Fingerprint conditionsValue = 0UL;
			foreach (var cond in conditions)
			{
				if (cond.value)
				{
					conditionsValue |= cond.feature;
				}
				mask |= (ulong)cond.feature;
			}

			var subFacts = _FingerprintsCount
				.Where(fp => ((ulong)fp.Key & mask) == ((ulong)conditionsValue & mask))
				.ToDictionary(fp => fp.Key, fp => fp.Value);
			return new FingerprintDistribution(subFacts);
		}

		public Fingerprint PickFingerprint(Random random)
		{
			if (random == null)
				throw new ArgumentNullException(nameof(random));
			Span<byte> b = stackalloc byte[8];
			random.NextBytes(b);
			return PickFingerprint(MemoryMarshal.Cast<byte, ulong>(b)[0]);
		}
		public Fingerprint PickFingerprint(ulong index)
		{
			index = index % (ulong)TotalCount;
			ulong currentRange = 0;
			foreach (var fp in _FingerprintsCount)
			{
				ulong nextRange = currentRange + (ulong)fp.Value;
				if (index < nextRange)
					return fp.Key;
				currentRange = nextRange;
			}
			throw new NotSupportedException("This should never happen BUG!");
		}
		public double GetProbabilityOf(Fingerprint fingerprint)
		{
			if (!_FingerprintsCount.TryGetValue(fingerprint, out int count))
				return 0.0;
			return (double)count / TotalCount;
		}
		public double GetProbabilityOf(params (Fingerprint feature, bool value)[] conditions)
		{
			ulong mask = 0UL;
			Fingerprint conditionsValue = 0UL;
			foreach (var cond in conditions)
			{
				if (cond.value)
				{
					conditionsValue |= cond.feature;
				}
				mask |= (ulong)cond.feature;
			}
			var subFacts = _FingerprintsCount
				.Where(fp => ((ulong)fp.Key & mask) == ((ulong)conditionsValue & mask))
				.Select(fp => fp.Value)
				.Sum();
			return (double)subFacts / TotalCount;
		}

		public static Fingerprint CalculateFingerprint(Transaction tx)
		{
			if (tx == null)
				throw new ArgumentNullException(nameof(tx));
			Fingerprint feature = 0UL;
			if (tx.Version == 1)
				feature |= Fingerprint.V1;
			if (tx.Version == 2)
				feature |= Fingerprint.V2;
			if (tx.LockTime == LockTime.Zero)
				feature |= Fingerprint.TimelockZero;
			if (tx.HasWitness)
				feature |= Fingerprint.HasWitness;

			var hasLowR = tx.Inputs.All(IsLowR);
			if (hasLowR)
			{
				feature |= Fingerprint.LowR;
			}
			var groupedScriptPubKeys =
					tx.Inputs.Select(txin => GetTxInType(txin))
							 .GroupBy(s => s)
							 .ToList();

			if (groupedScriptPubKeys.Count > 1)
			{
				feature |= Fingerprint.SpendFromMixed;
			}
			else if (groupedScriptPubKeys[0].Key != 0)
			{
				feature |= groupedScriptPubKeys[0].Key;
			}

			var groupedSequences = tx.Inputs.Select(txin => txin.Sequence)
					.GroupBy(s => s)
					.ToList();

			if (groupedSequences.Count > 1)
			{
				feature |= Fingerprint.SequenceMixed;
			}
			else if (groupedSequences[0].Key == 0)
			{
				feature |= Fingerprint.SequenceAllZero;
			}
			else if (groupedSequences[0].Key == Sequence.Final)
			{
				feature |= Fingerprint.SequenceAllFinal;
			}
			else if (groupedSequences[0].Key == Sequence.OptInRBF)
			{
				feature |= Fingerprint.SequenceAllMinus2;
			}

			if (groupedSequences.Count == 1 && groupedSequences[0].Key != Sequence.Final)
			{
				if (tx.LockTime != LockTime.Zero)
					feature |= Fingerprint.FeeSniping;
			}

			if (tx.RBF)
			{
				feature |= Fingerprint.RBF;
			}
			return feature;
		}

		private static bool IsLowR(TxIn txin)
		{
			IEnumerable<byte[]> pushes = txin.WitScript.PushCount > 0 ? txin.WitScript.Pushes :
									   txin.ScriptSig.IsPushOnly ? txin.ScriptSig.ToOps().Select(o => o.PushData) :
										new byte[0][];
			return pushes.Where(p => ECDSASignature.IsValidDER(p)).All(p => p.Length <= 71);
		}

		private static Fingerprint GetTxInType(TxIn txin)
		{
			if (txin.WitScript.PushCount == 0)
			{
				if (PayToPubkeyHashTemplate.Instance.CheckScriptSig(txin.ScriptSig, null))
					return Fingerprint.SpendFromP2PKH;
				if (PayToScriptHashTemplate.Instance.CheckScriptSig(txin.ScriptSig, null))
					return Fingerprint.SpendFromP2SHLegacy;
			}
			else if (txin.ScriptSig.Length == 0)
			{
				if (PayToWitPubKeyHashTemplate.Instance.ExtractWitScriptParameters(txin.WitScript) is { })
					return Fingerprint.SpendFromP2WPKH;
				if (PayToWitScriptHashTemplate.Instance.ExtractWitScriptParameters(txin.WitScript, null) is { })
					return Fingerprint.SpendFromP2WSH;
			}
			else if (PayToScriptHashTemplate.Instance.CheckScriptSig(txin.ScriptSig, null))
			{
				if (PayToWitPubKeyHashTemplate.Instance.ExtractWitScriptParameters(txin.WitScript) is { })
					return Fingerprint.SpendFromP2SHP2WPKH;
				if (PayToWitScriptHashTemplate.Instance.ExtractWitScriptParameters(txin.WitScript) is { })
					return Fingerprint.SpendFromP2SHP2WSH;
			}
			return 0;
		}
	}
}
