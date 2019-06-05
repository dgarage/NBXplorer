using NBitcoin;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using NBXplorer.DerivationStrategy;
using System.Threading.Tasks;
using NBitcoin.RPC;
using System.Threading;

namespace NBXplorer.Tests
{
	public static class Extensions
	{
		public static NewTransactionEvent WaitForTransaction(this LongPollingNotificationSession session, DerivationStrategyBase derivationStrategy, uint256 txId)
		{
			return session.WaitForTransaction(TrackedSource.Create(derivationStrategy, session.Client.Network.NBitcoinNetwork), txId);
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

		static BitcoinAddress Dummy = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main);
		public static async Task<KeyPathInformation> GetKeyInformation(this Repository repo, Script script)
		{
			return (await repo.GetKeyInformations(new Script[] { script }))[script].SingleOrDefault();
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
