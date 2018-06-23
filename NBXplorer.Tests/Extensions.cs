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
	}

}
