using NBitcoin;
using NBXplorer.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBXplorer.Models;

namespace NBXplorer
{
	/// <summary>
	/// This class represents a set of two UTXOState, the Known set, which can be deduced by the UTXO hash set by the client. And the actual UTXOState.
	/// </summary>
	public class UTXOStates
	{
		public UTXOStates()
		{
		}

		/// <summary>
		/// UTXOState that is already known
		/// </summary>
		public UTXOState Known
		{
			get; set;
		}

		/// <summary>
		/// Actual UTXOState
		/// </summary>
		public UTXOState Actual
		{
			get; set;
		}
	}

	/// <summary>
	/// UTXOStateResult represents the state of UTXOs after a set on confirmed transaction has been applied to it and after a set of unconfirmed has been apply
	/// </summary>
	public class UTXOStateResult
	{
		public static UTXOStateResult CreateStates(IEnumerable<TrackedTransaction> unconfirmed, IEnumerable<TrackedTransaction> confirmed)
		{
			var utxoState = new UTXOState();

			foreach(var tx in confirmed)
			{
				if(utxoState.Apply(tx) == ApplyTransactionResult.Conflict)
					throw new InvalidOperationException("Conflict in UTXOStateResult.CreateStates should never happen");
			}


			var actualConf = utxoState.Snapshot();
			foreach(var tx in unconfirmed)
			{
				if(utxoState.Apply(tx) == ApplyTransactionResult.Conflict)
					throw new InvalidOperationException("Conflict in UTXOStateResult.CreateStates should never happen");
			}

			var actualUnconf = utxoState;

			return new UTXOStateResult()
			{
				Unconfirmed = new UTXOStates()
				{
					Actual = actualUnconf
				},
				Confirmed = new UTXOStates()
				{
					Actual = actualConf,
				}
			};
		}

		/// <summary>
		/// State of the UTXO after confirmed transaction applied
		/// </summary>
		public UTXOStates Confirmed
		{
			get; set;
		}

		/// <summary>
		/// State of the UTXO after unconfirmed and confirmed transaction applied
		/// </summary>
		public UTXOStates Unconfirmed
		{
			get; set;
		}
	}
}
