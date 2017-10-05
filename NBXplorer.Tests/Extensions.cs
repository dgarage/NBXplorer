using NBitcoin;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using NBXplorer.DerivationStrategy;

namespace NBXplorer.Tests
{
	public static class Extensions
	{
		public static KeyPathInformation GetKeyInformation(this Repository repo, Script script)
		{
			return repo.GetKeyInformations(script).GetAwaiter().GetResult().SingleOrDefault();
		}
		public static void MarkAsUsed(this Repository repo, KeyPathInformation info)
		{
			repo.MarkAsUsedAsync(info).GetAwaiter().GetResult();
		}

		public static void Track(this Repository repo, DerivationStrategyBase derivation)
		{
			repo.TrackAsync(derivation).GetAwaiter().GetResult();
		}
	}
}
