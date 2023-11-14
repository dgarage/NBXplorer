using System;

namespace NBXplorer.Analytics
{
	[Flags]
	public enum Fingerprint : ulong
	{
		V1 = 0b_1UL,
		V2 = 0b_10UL,
		SpendFromP2PKH = 0b_100UL,
		SpendFromP2SHLegacy = 0b_1000UL,
		SpendFromP2SHP2WPKH = 0b_10000UL,
		SpendFromP2SHP2WSH = 0b_100000UL,
		SpendFromP2WSH = 0b_1000000UL,
		SpendFromP2WPKH = 0b_10000000UL,
		SpendFromMixed = 0b_100000000UL,
		HasWitness = 0b_1000000000UL,
		LowR = 0b_10000000000UL,
		TimelockZero = 0b_100000000000UL,
		SequenceAllZero = 0b_1000000000000UL,
		RBF = 0b_10000000000000UL,
		FeeSniping = 0b_100000000000000UL,
		SequenceAllMinus2 = 0b_1000000000000000UL,
		SequenceAllMinus1 = 0b_10000000000000000UL,
		SequenceAllFinal = 0b_100000000000000000UL,
		SequenceMixed = 0b_1000000000000000000UL,
	}
}
