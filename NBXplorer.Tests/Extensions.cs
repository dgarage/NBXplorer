using NBitcoin;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using NBXplorer.DerivationStrategy;
using NBitcoin.RPC;
using System.Threading;

namespace NBXplorer.Tests
{
	public static class Extensions
	{
		public static void WaitForTransaction(this LongPollingNotificationSession session, DerivationStrategyBase derivationStrategy, uint256 txId)
		{
			using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
			{
				while(true)
				{
					if (session.NextEvent(cts.Token) is NewTransactionEvent evts)
					{
						if (evts.DerivationStrategy == derivationStrategy && evts.TransactionData.TransactionHash == txId)
						{
							break;
						}
					}
				}
			}
		}

		public static void WaitForBlocks(this LongPollingNotificationSession session, params uint256[] txIds)
		{
			if (txIds == null || txIds.Length == 0)
				return;
			HashSet<uint256> txidsSet = new HashSet<uint256>(txIds);
			using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
			{
				while (true)
				{
					if (session.NextEvent(cts.Token) is NewBlockEvent evts)
					{
						txidsSet.Remove(evts.Hash);
						if (txidsSet.Count == 0)
							break;
					}
				}
			}
		}

		public static IEnumerable<Transaction> TopologicalSort(this IEnumerable<Transaction> transactions)
		{
			return transactions
				.Select(t => t.AsAnnotatedTransaction()).ToList()
				.TopologicalSort()
				.Select(t => t.Record.Transaction);
		}

		static BitcoinAddress Dummy = new Key().PubKey.GetAddress(Network.Main);
		static AnnotatedTransaction AsAnnotatedTransaction(this Transaction tx)
		{
			return new AnnotatedTransaction(new TrackedTransaction(new TrackedTransactionKey(tx.GetHash(), null, false), new AddressTrackedSource(Dummy), tx, new Dictionary<Script, KeyPath>()), null);
		}
		public static KeyPathInformation GetKeyInformation(this Repository repo, Script script)
		{
			return repo.GetKeyInformations(new Script[] { script }).GetAwaiter().GetResult()[script].SingleOrDefault();
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
