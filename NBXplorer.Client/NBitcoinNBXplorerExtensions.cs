#nullable enable
using System;
using System.Collections.Generic;
using NBXplorer.DerivationStrategy;

namespace NBitcoin;

public static class NBitcoinNBXplorerExtensions
{
	/// <summary>
	/// Filter the keys which contains the <paramref name="accountKey"/> and <paramref name="accountKeyPath"/> in the HDKeys and whose input/output
	/// the same scriptPubKeys as <paramref name="derivationStrategy"/>.
	/// </summary>
	/// <param name="psbt">The PSBT from which to get the keys</param>
	/// <param name="derivationStrategy">The derivation scheme</param>
	/// <param name="accountKey">The account key that will be used to sign (i.e., 49'/0'/0')</param>
	/// <param name="accountKeyPath">The account key path</param>
	/// <returns>HD Keys matching master root key</returns>
	public static IEnumerable<PSBTHDKeyMatch> HDKeysFor(this PSBT psbt, DerivationStrategyBase derivationStrategy, IHDKey accountKey,
		RootedKeyPath? accountKeyPath)
	{
		if (ToHDScriptPubKey(derivationStrategy, accountKey) is {} hd)
			return psbt.HDKeysFor(hd, accountKey, accountKeyPath);
		return Array.Empty<PSBTHDKeyMatch>();
	}
	/// <summary>
	/// Filter the keys that contain the <paramref name="accountKey"/> in the HDKeys and whose input/output
	/// the same scriptPubKeys as <paramref name="derivationStrategy"/>.
	/// </summary>
	/// <param name="psbt">The PSBT from which to get the keys</param>
	/// <param name="derivationStrategy">The derivation scheme</param>
	/// <param name="accountKey">The account key that will be used to sign (i.e., 49'/0'/0')</param>
	/// <returns>HD Keys matching master root key</returns>
	public static IEnumerable<PSBTHDKeyMatch> HDKeysFor(this PSBT psbt, DerivationStrategyBase derivationStrategy, IHDKey accountKey)
	=> HDKeysFor(psbt, derivationStrategy, accountKey, null);
	
	/// <summary>
	/// Filter the keys which contains the <paramref name="accountKey"/> and <paramref name="accountKeyPath"/> in the HDKeys and whose input/output
	/// the same scriptPubKeys as <paramref name="derivationStrategy"/>.
	/// </summary>
	/// <param name="coin">The coins to get the keys from</param>
	/// <param name="derivationStrategy">The derivation scheme</param>
	/// <param name="accountKey">The account key that will be used to sign (i.e., 49'/0'/0')</param>
	/// <param name="accountKeyPath">The account key path</param>
	/// <returns>HD Keys matching master root key</returns>
	public static IEnumerable<PSBTHDKeyMatch> HDKeysFor(this PSBTCoin coin, DerivationStrategyBase derivationStrategy, IHDKey accountKey,
		RootedKeyPath? accountKeyPath)
	{
		if (ToHDScriptPubKey(derivationStrategy, accountKey) is {} hd)
			return coin.HDKeysFor(hd, accountKey, accountKeyPath);
		return Array.Empty<PSBTHDKeyMatch>();
	}

	static IHDScriptPubKey? ToHDScriptPubKey(DerivationStrategyBase derivationStrategy, IHDKey accountKey)
	{
		if (derivationStrategy is null)
			throw new ArgumentNullException(nameof(derivationStrategy));
		if (derivationStrategy is StandardDerivationStrategyBase standard)
			return standard;
#if !NO_RECORD
		else if (derivationStrategy is PolicyDerivationStrategy policy && policy.GetHDScriptPubKey(accountKey) is IHDScriptPubKey hd)
			return hd;
#endif
		return null;
	}

	/// <summary>
	/// Filter the keys that contain the <paramref name="accountKey"/> in the HDKeys and whose input/output
	/// the same scriptPubKeys as <paramref name="derivationStrategy"/>.
	/// </summary>
	/// <param name="coin">The coins to get the keys from</param>
	/// <param name="derivationStrategy">The derivation scheme</param>
	/// <param name="accountKey">The account key that will be used to sign (i.e., 49'/0'/0')</param>
	/// <returns>HD Keys matching master root key</returns>
	public static IEnumerable<PSBTHDKeyMatch> HDKeysFor(this PSBTCoin coin, DerivationStrategyBase derivationStrategy, IHDKey accountKey)
		=> HDKeysFor(coin, derivationStrategy, accountKey, null);


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
		if (ToHDScriptPubKey(derivationStrategy, accountKey) is {} hd)
			return psbt.GetBalance(hd, accountKey, accountKeyPath);
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
		if (ToHDScriptPubKey(derivationStrategy, accountKey) is {} hd)
			return psbt.SignAll(hd, accountKey, accountKeyPath);
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