#nullable enable
using System;
using NBXplorer.DerivationStrategy;

namespace NBitcoin;

public static class NBitcoinNBXplorerExtensions
{
	/// <summary>
	/// Get the balance change if you were signing this transaction.
	/// </summary>
	/// <param name="psbt">The PSBT from which to get the balance</param>
	/// <param name="derivationStrategy">The derivation scheme</param>
	/// <param name="accountKey">The account key that will be used to sign (i.e., 49'/0'/0')</param>
	/// <param name="accountKeyPath">The account key path</param>
	/// <returns>The balance change</returns>
	public static Money GetBalance(this PSBT psbt, DerivationStrategyBase derivationStrategy, IHDKey accountKey, RootedKeyPath? accountKeyPath)
	{
		if (derivationStrategy is null)
			throw new ArgumentNullException(nameof(derivationStrategy));
		if (derivationStrategy is StandardDerivationStrategyBase standard)
			return psbt.GetBalance(standard, accountKey, accountKeyPath);
#if !NO_RECORD
		else if (derivationStrategy is PolicyDerivationStrategy policy && policy.GetHDScriptPubKey(accountKey) is IHDScriptPubKey hd)
			return psbt.GetBalance(hd, accountKey, accountKeyPath);
#endif
		return Money.Zero;
	}

	/// <summary>
	/// Get the balance change if you were signing this transaction.
	/// </summary>
	/// <param name="psbt">The PSBT from which to get the balance</param>
	/// <param name="derivationStrategy">The derivation scheme</param>
	/// <param name="accountKey">The account key that will be used to sign (i.e., 49'/0'/0')</param>
	/// <returns>The balance change</returns>
	public static Money GetBalance(this PSBT psbt, DerivationStrategyBase derivationStrategy, IHDKey accountKey)
	=> GetBalance(psbt, derivationStrategy, accountKey, null);

	/// <summary>
	/// Sign all inputs that derive addresses from <paramref name="derivationStrategy"/> and that need to be signed by <paramref name="accountKey"/>.
	/// </summary>
	/// <param name="psbt">The PSBT to sign</param>
	/// <param name="derivationStrategy">The derivation scheme</param>
	/// <param name="accountKey">The account key with which to sign</param>
	/// <param name="accountKeyPath">The account key path (eg. [masterFP]/49'/0'/0')</param>
	/// <returns>The signed PSBT</returns>
	public static PSBT SignAll(this PSBT psbt, DerivationStrategyBase derivationStrategy, IHDKey accountKey, RootedKeyPath? accountKeyPath)
	{
		if (derivationStrategy is null)
			throw new ArgumentNullException(nameof(derivationStrategy));
		if (derivationStrategy is StandardDerivationStrategyBase standard)
			return psbt.SignAll(standard, accountKey, accountKeyPath);
#if !NO_RECORD
		else if (derivationStrategy is PolicyDerivationStrategy policy && policy.GetHDScriptPubKey(accountKey) is IHDScriptPubKey hd)
			return psbt.SignAll(hd, accountKey, accountKeyPath);
#endif
		return psbt;
	}

	/// <summary>
	/// Sign all inputs that derive addresses from <paramref name="derivationStrategy"/> and that need to be signed by <paramref name="accountKey"/>.
	/// </summary>
	/// <param name="psbt">The PSBT to sign</param>
	/// <param name="derivationStrategy">The derivation scheme</param>
	/// <param name="accountKey">The account key with which to sign</param>
	/// <returns>The signed PSBT</returns>
	public static PSBT SignAll(this PSBT psbt, DerivationStrategyBase derivationStrategy, IHDKey accountKey)
	=> SignAll(psbt, derivationStrategy, accountKey, null);
}