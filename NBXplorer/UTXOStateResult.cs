using NBitcoin;
using NBXplorer.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace NBXplorer
{
	public class UTXOStates
	{
		public UTXOStates()
		{
		}
		public UTXOState Known
		{
			get; set;
		}
		public UTXOState Actual
		{
			get; set;
		}
	}
	public class UTXOStateResult
	{
		public static UTXOStateResult CreateStates(
			Func<Script, bool> matchScript,
			uint256 knownUnconfHash, IEnumerable<Transaction> unconfirmed, 
			uint256 knownConfHash, IEnumerable<Transaction> confirmed)
		{
			var utxoState = new UTXOState();
			utxoState.MatchScript = matchScript;

			var knownConf = knownConfHash == uint256.Zero ? new UTXOState() : null;
			foreach(var tx in confirmed)
			{
				var applyResult = utxoState.Apply(tx);
				if(applyResult == ApplyTransactionResult.Conflict)
				{
					Logs.Explorer.LogError("A conflict among confirmed transaction happened, this should be impossible");
					throw new InvalidOperationException("The impossible happened");
				}

				if(applyResult == ApplyTransactionResult.Passed)
				{
					if(utxoState.CurrentHash == knownConfHash)
						knownConf = utxoState.Snapshot();
				}
			}


			var actualConf = utxoState.Snapshot();
			utxoState.ResetEvents();

			var knownUnconf = knownUnconfHash == uint256.Zero ? utxoState.Snapshot() : null;
			foreach(var tx in unconfirmed)
			{
				var txid = tx.GetHash();
				if(utxoState.Apply(tx) == ApplyTransactionResult.Passed)
				{
					if(utxoState.CurrentHash == knownUnconfHash)
						knownUnconf = utxoState.Snapshot();
				}
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

		public UTXOStates Confirmed
		{
			get; set;
		}

		public UTXOStates Unconfirmed
		{
			get; set;
		}
	}
}
