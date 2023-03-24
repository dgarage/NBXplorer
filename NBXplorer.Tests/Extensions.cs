using NBitcoin;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using NBXplorer.DerivationStrategy;
using NBitcoin.RPC;
using System.Threading;
using NBXplorer.Backends;

namespace NBXplorer.Tests
{
	public static class Extensions
	{
		public static NewTransactionEvent WaitForTransaction(this LongPollingNotificationSession session, DerivationStrategyBase derivationStrategy, uint256 txId)
		{
			return session.WaitForTransaction(TrackedSource.Create(derivationStrategy), txId);
		}
		public static NewTransactionEvent WaitForTransaction(this LongPollingNotificationSession session, BitcoinAddress address, uint256 txId)
		{
			return session.WaitForTransaction(TrackedSource.Create(address), txId);
		}
		public static NewTransactionEvent WaitForTransaction(this LongPollingNotificationSession session, TrackedSource trackedSource, uint256 txId)
		{
			using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
			{
				while (true)
				{
					if (session.NextEvent(cts.Token) is NewTransactionEvent evts)
					{
						if (evts.TrackedSource == trackedSource && evts.TransactionData.TransactionHash == txId)
						{
							return evts;
						}
					}
				}
			}
		}
		public static void WaitForBlocks(this LongPollingNotificationSession session, params uint256[] blockIds)
		{
			if (blockIds == null || blockIds.Length == 0)
				return;
			HashSet<uint256> txidsSet = new HashSet<uint256>(blockIds);
			using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
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

		public static DerivationLine GetLineFor(this DerivationStrategyBase strategy, DerivationFeature feature)
		{
			return strategy.GetLineFor(KeyPathTemplates.Default.GetKeyPathTemplate(feature));
		}

		static BitcoinAddress Dummy = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main);
		public static KeyPathInformation GetKeyInformation(this IRepository repo, Script script)
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
