using NBitcoin;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using NBXplorer.DerivationStrategy;
using NBitcoin.RPC;

namespace NBXplorer.Tests
{
	public static class Extensions
	{
		public static IEnumerable<Transaction> TopologicalSort(this IEnumerable<Transaction> transactions)
		{
			return transactions
				.Select(t => t.AsAnnotatedTransaction())
				.TopologicalSort()
				.Select(t => t.Record.Transaction);
		}

		static AnnotatedTransaction AsAnnotatedTransaction(this Transaction tx)
		{
			return new AnnotatedTransaction(new TrackedTransaction(new TrackedTransactionKey(tx.GetHash(), null, false), tx, new Repository.TransactionMiniMatch()), null);
		}
		public static KeyPathInformation GetKeyInformation(this Repository repo, Script script)
		{
			return repo.GetKeyInformations(new Script[] { script })[script].SingleOrDefault();
		}

		public static uint256[] EnsureGenerate(this RPCClient client, int blockCount)
		{
			return client.EnsureGenerateAsync(blockCount).GetAwaiter().GetResult();
		}

		public static RPCClient WithCapabilitiesOf(this RPCClient client, RPCClient target)
		{
			client.Capabilities = target.Capabilities;
			return client;
		}
	}

}
