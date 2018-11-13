using NBitcoin;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer.Events
{
    public class NewTransactionMatchEvent
    {
		public NewTransactionMatchEvent(string cryptoCode, uint256 blockId, TrackedTransaction trackedTransaction, Repository.SavedTransaction savedTransaction)
		{
			TrackedTransaction = trackedTransaction;
			BlockId = blockId;
			SavedTransaction = savedTransaction;
			CryptoCode = cryptoCode;
		}

		public string CryptoCode
		{
			get; set;
		}

		public uint256 BlockId
		{
			get; set;
		}

		public TrackedTransaction TrackedTransaction
		{
			get; set;
		}
		public Repository.SavedTransaction SavedTransaction
		{
			get; set;
		}

		public override string ToString()
		{
			var conf = (BlockId == null ? "unconfirmed" : "confirmed");

			string strategy = TrackedTransaction.TrackedSource.ToPrettyString();

			string keyPathSuffix = string.Empty;
			if (TrackedTransaction.KnownKeyPathMapping.Count != 0)
			{
				var keyPaths = TrackedTransaction.KnownKeyPathMapping.Values.Select(v => v.ToString()).ToArray();
				keyPathSuffix = $" ({String.Join(", ", keyPaths)})";
			}
			return $"{CryptoCode}: {strategy} matching {conf} transaction {TrackedTransaction.TransactionHash.ToPrettyString()}{keyPathSuffix}";
		}
	}
}