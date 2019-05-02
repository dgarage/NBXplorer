using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace NBXplorer.Models
{
	public class CreatePSBTRequest
	{
		/// <summary>
		/// A seed to specific to get a deterministic PSBT (useful for tests)
		/// </summary>
		public int? Seed { get; set; }

		/// <summary>
		///	The version of the transaction (Optional, default to 1)
		/// </summary>
		public uint? Version { get; set; }
		/// <summary>
		/// The timelock of the transaction, activate RBF if not null (Optional: null, nLockTime to 0)
		/// </summary>
		public LockTime? LockTime { get; set; }
		/// <summary>
		/// Whether this transaction should use RBF or not.
		/// </summary>
		public bool RBF { get; set; }
		/// <summary>
		/// The destinations where to send the money
		/// </summary>
		public List<CreatePSBTDestination> Destinations { get; set; } = new List<CreatePSBTDestination>();
		/// <summary>
		/// Fee settings
		/// </summary>
		public FeePreference FeePreference { get; set; }
		/// <summary>
		/// Whether the creation of this PSBT will reserve a new change address
		/// </summary>
		public bool ReserveChangeAddress { get; set; }

		/// <summary>
		/// Default to 0, the minimum confirmations a UTXO need to be selected. (by default unconfirmed and confirmed UTXO will be used)
		/// </summary>
		public int MinConfirmations { get; set; }

		/// <summary>
		/// Do not select the following outpoints for creating the PSBT (default to empty)
		/// </summary>
		public List<OutPoint> ExcludeOutpoints { get; set; }
		/// <summary>
		/// Only select the following outpoints for creating the PSBT (default to null)
		/// </summary>
		public List<OutPoint> IncludeOnlyOutpoints { get; set; }

		/// <summary>
		/// Use a specific change address (Optional, default: null, mutually exclusive with ReserveChangeAddress)
		/// </summary>
		public BitcoinAddress ExplicitChangeAddress { get; set; }
	}
	public class CreatePSBTDestination
	{
		public BitcoinAddress Destination { get; set; }
		/// <summary>
		/// Will Send this amount to this destination (Mutually exclusive with: SweepAll)
		/// </summary>
		public Money Amount { get; set; }
		/// <summary>
		/// Will substract the fees of this transaction to this destination (Mutually exclusive with: SweepAll)
		/// </summary>
		public bool SubstractFees { get; set; }
		/// <summary>
		/// Will sweep all the balance of your wallet to this destination (Mutually exclusive with: Amount, SubstractFees)
		/// </summary>
		public bool SweepAll { get; set; }
	}
	public class FeePreference
	{
		/// <summary>
		/// An explicit fee rate for the transaction in Satoshi per vBytes (Mutually exclusive with: BlockTarget, ExplicitFee, FallbackFeeRate)
		/// </summary>
		public FeeRate ExplicitFeeRate { get; set; }
		/// <summary>
		/// An explicit fee for the transaction in Satoshi (Mutually exclusive with: BlockTarget, ExplicitFeeRate, FallbackFeeRate)
		/// </summary>
		public Money ExplicitFee { get; set; }
		/// <summary>
		/// A number of blocks after which the user expect one confirmation (Mutually exclusive with: ExplicitFeeRate, ExplicitFee)
		/// </summary>
		public int? BlockTarget { get; set; }
		/// <summary>
		/// If the NBXplorer's node does not have proper fee estimation, this specific rate will be use in Satoshi per vBytes. (Mutually exclusive with: ExplicitFeeRate, ExplicitFee)
		/// </summary>
		public FeeRate FallbackFeeRate { get; set; }
	}
}
