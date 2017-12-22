using NBitcoin;
using NBXplorer.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.Extensions.Logging;

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
		public static UTXOStateResult CreateStates(
			Func<Script[], bool[]> matchScript,
			uint256 knownUnconfHash, IEnumerable<Transaction> unconfirmed,
			uint256 knownConfHash, IEnumerable<Transaction> confirmed)
		{
			var utxoState = new UTXOState();
			utxoState.MatchScript = matchScript;

			var knownConf = knownConfHash == uint256.Zero ? new UTXOState() : null;
			foreach(var tx in confirmed)
			{
				if(utxoState.Apply(tx) == ApplyTransactionResult.Conflict)
					throw new InvalidOperationException("Conflict in UTXOStateResult.CreateStates should never happen");
				if(utxoState.CurrentHash == knownConfHash)
					knownConf = utxoState.Snapshot();
			}


			var actualConf = utxoState.Snapshot();
			utxoState.ResetEvents();

			var knownUnconf = knownUnconfHash == uint256.Zero ? utxoState.Snapshot() : null;
			foreach(var tx in unconfirmed)
			{
				var txid = tx.GetHash();
				if(utxoState.Apply(tx) == ApplyTransactionResult.Conflict)
					throw new InvalidOperationException("Conflict in UTXOStateResult.CreateStates should never happen");

				if(utxoState.CurrentHash == knownUnconfHash)
					knownUnconf = utxoState.Snapshot();
			}

			var actualUnconf = utxoState;

			return new UTXOStateResult()
			{
				Unconfirmed = new UTXOStates()
				{
					Known = knownUnconf,
					Actual = actualUnconf
				},
				Confirmed = new UTXOStates()
				{
					Known = knownConf,
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
