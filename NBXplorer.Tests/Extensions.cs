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
		public static KeyPathInformation GetKeyInformation(this Repository repo, Script script)
		{
			return repo.GetKeyInformations(new Script[] { script })[script].SingleOrDefault();
		}

		public static uint256[] EnsureGenerate(this RPCClient client, int blockCount)
		{
			return client.EnsureGenerateAsync(blockCount).GetAwaiter().GetResult();
		}

		public static Transaction SignRawTransaction(this ServerTester tester, Transaction transaction)
		{
			return SignRawTransaction(tester.RPC, transaction, tester);
		}

		public static Transaction SignRawTransaction(this RPCClient client, Transaction transaction, ServerTester tester)
		{
			if (tester.RPCSupportSignRawTransaction)
			{
#pragma warning disable CS0618 // Type or member is obsolete
				return client.SignRawTransaction(transaction);
#pragma warning restore CS0618 // Type or member is obsolete
			}
			else
			{
				return client.SignRawTransactionWithWallet(new SignRawTransactionRequest()
				{
					Transaction = transaction
				}).SignedTransaction;
			}
		}
	}

}
