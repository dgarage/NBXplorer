#nullable enable
using System;
using NBXplorer.DerivationStrategy;

namespace NBitcoin;

public static class NBitcoinNBXplorerExtensions
{
	public static Money GetBalance(this PSBT psbt, DerivationStrategyBase derivationStrategy, IHDKey accountKey)
	{
		if (derivationStrategy is null)
			throw new ArgumentNullException(nameof(derivationStrategy));
		if (derivationStrategy is StandardDerivationStrategyBase standard)
			return psbt.GetBalance(standard, accountKey);
#if !NO_RECORD
		else if (derivationStrategy is PolicyDerivationStrategy policy && policy.GetHDScriptPubKey(accountKey) is IHDScriptPubKey hd)
			return psbt.GetBalance(hd, accountKey);
#endif
		return Money.Zero;
	}
	public static PSBT SignAll(this PSBT psbt, DerivationStrategyBase derivationStrategy, IHDKey accountKey)
	{
		if (derivationStrategy is null)
			throw new ArgumentNullException(nameof(derivationStrategy));
		if (derivationStrategy is StandardDerivationStrategyBase standard)
			return psbt.SignAll(standard, accountKey);
#if !NO_RECORD
		else if (derivationStrategy is PolicyDerivationStrategy policy && policy.GetHDScriptPubKey(accountKey) is IHDScriptPubKey hd)
			return psbt.SignAll(hd, accountKey);
#endif
		return psbt;
	}
}