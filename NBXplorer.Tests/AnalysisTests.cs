using NBitcoin;
using System.Linq;
using System.IO;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using System.Net.Http;
using Xunit.Abstractions;
using NBXplorer.Analytics;
using NBXplorer.DerivationStrategy;
using NBitcoin.Altcoins;

namespace NBXplorer.Tests
{
	public class AnalysisTests
	{
		private readonly ITestOutputHelper Log;

		public AnalysisTests(ITestOutputHelper testOutputHelper)
		{
			this.Log = testOutputHelper;
		}

		[Fact]
		public void CanDoDistributionArithmetic()
		{
			var facts1 = new FingerprintDistribution(new Dictionary<Fingerprint, int>()
			{
				{ Fingerprint.V1, 9 },
				{ Fingerprint.V2, 10 },
				{ (Fingerprint.V1 | Fingerprint.SpendFromP2WPKH), 11 },
			});
			var facts2 = new FingerprintDistribution(new Dictionary<Fingerprint, int>()
			{
				{ Fingerprint.V1, 19 },
				{ Fingerprint.V2, 11 },
				{ (Fingerprint.V1 | Fingerprint.SpendFromP2WPKH), 41 },
			});
			var fact3 = facts1 + facts2;
			Assert.Equal(9 + 10 + 11 + 19 + 11 + 41, fact3.TotalCount);
			Assert.Equal(9 + 19, fact3.GetFingerprintCount(Fingerprint.V1));
			Assert.Equal(10 + 11, fact3.GetFingerprintCount(Fingerprint.V2));
			Assert.Equal(11 + 41, fact3.GetFingerprintCount(Fingerprint.V1 | Fingerprint.SpendFromP2WPKH));
			Assert.Equal(0, fact3.GetFingerprintCount(Fingerprint.RBF));
			fact3 = fact3 - facts1;
			Assert.Equal(19 + 11 + 41, fact3.TotalCount);
			Assert.Equal(19, fact3.GetFingerprintCount(Fingerprint.V1));
			Assert.Equal(11, fact3.GetFingerprintCount(Fingerprint.V2));
			Assert.Equal(41, fact3.GetFingerprintCount(Fingerprint.V1 | Fingerprint.SpendFromP2WPKH));
			Assert.Equal(0, fact3.GetFingerprintCount(Fingerprint.RBF));
		}

		[Fact]
		public void CheckConsistent()
		{
			var facts = new FingerprintDistribution(new Dictionary<Fingerprint, int>()
			{
				{ Fingerprint.V1, 9 },
				{ Fingerprint.V2, 10 },
				{ (Fingerprint.V1 | Fingerprint.SpendFromP2WPKH), 11 },
			});

			Assert.Equal(30, facts.TotalCount);
			Assert.Equal(20.0 / 30.0, facts.GetProbabilityOf((Fingerprint.V1, true)));
			Assert.Equal(11.0 / 30.0, facts.GetProbabilityOf((Fingerprint.V1, true), (Fingerprint.SpendFromP2WPKH, true)));
			Assert.Equal(9.0 / 30.0, facts.GetProbabilityOf((Fingerprint.V1, true), (Fingerprint.SpendFromP2WPKH, false)));

			var originalFacts = facts;
			facts = facts.KnowingThat((Fingerprint.V1, true));
			Assert.Equal(20, facts.TotalCount);
			Assert.Equal(1.0, facts.GetProbabilityOf((Fingerprint.V1, true)));
			Assert.Equal(11.0 / 20.0, facts.GetProbabilityOf((Fingerprint.SpendFromP2WPKH, true)));

			facts = originalFacts;
			facts = facts.KnowingThat((Fingerprint.V2, false));
			Assert.Equal(20, facts.TotalCount);
			Assert.Equal(1.0, facts.GetProbabilityOf((Fingerprint.V1, true)));
			Assert.Equal(11.0 / 20.0, facts.GetProbabilityOf((Fingerprint.SpendFromP2WPKH, true)));

			facts = new FingerprintDistribution(new Dictionary<Fingerprint, int>()
		{
			{ Fingerprint.V1, 9 },
			{ Fingerprint.V2, 10 },
			{ (Fingerprint.V1 | Fingerprint.SpendFromP2WPKH), 111 },
		});
			Random r = new Random();
			var randomPicks = Enumerable
				.Range(0, 100_000)
				.Select(_ => facts.PickFingerprint(r))
				.GroupBy(f => f)
				.ToDictionary(f => f.Key, f => f.Count());
			Assert.Equal(9.0 / 130.0, (double)randomPicks[Fingerprint.V1] / 100_000, 1);
			Assert.Equal(10.0 / 130.0, (double)randomPicks[Fingerprint.V2] / 100_000, 1);
			Assert.Equal(111.0 / 130.0, (double)randomPicks[(Fingerprint.V1 | Fingerprint.SpendFromP2WPKH)] / 100_000, 1);
		}

		[Fact]
		public async Task CanCalculateBlockStats()
		{
			var block = await GetBlock("0000000000000000000a5296b687eb7edadeb95952fc37c564418a189e16a3f0");
			var facts = FingerprintDistribution.Calculate(block);
			var maxSizeLabel = facts.RawDistribution.Select(f => f.Key.ToString().Length).Max();
			foreach (var kv in facts.RawDistribution.OrderByDescending(k => k.Value))
			{
				Log.WriteLine(kv.Key.ToString().PadRight(maxSizeLabel) + "|" + $"{kv.Value}".PadRight(5) + "".PadRight(kv.Value, '*'));
			}
		}

		[Fact]
		public async Task GenerateDefaultDistribution()
		{
			var block = await GetBlock("0000000000000000000a5296b687eb7edadeb95952fc37c564418a189e16a3f0");
			var facts = FingerprintDistribution.Calculate(block);
			var maxSizeLabel = facts.RawDistribution.Select(f => f.Key.ToString().Length).Max();
			foreach (var kv in facts.RawDistribution.OrderByDescending(k => k.Value))
			{
				Log.WriteLine($"{{ Fingerprint.{kv.Key.ToString().Replace(", ", " | Fingerprint.")}, {kv.Value} }},");
			}
		}

		async Task<Block> GetBlock(string blockId)
		{
			if (File.Exists(blockId))
			{
				try
				{
					return Block.Load(await File.ReadAllBytesAsync(blockId), Network.Main);
				}
				catch { }
			}
			using var client = new HttpClient();
			var resp = await client.GetAsync($"https://mempool.space/api/block/{blockId}/raw");
			resp.EnsureSuccessStatusCode();
			var bytes = await resp.Content.ReadAsByteArrayAsync();
			var block = Block.Load(bytes, Network.Main);
			await File.WriteAllBytesAsync(blockId, bytes);
			return block;
		}
	}
}
