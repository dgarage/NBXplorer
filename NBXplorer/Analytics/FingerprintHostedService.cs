using Microsoft.Extensions.Hosting;
using NBitcoin;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer.Analytics
{
	/// <summary>
	/// This listen new blocks, and computer the 5 block windows of fingerprint distribution
	/// </summary>
	public class FingerprintHostedService : IHostedService
	{
		const int BlockWindow = 5;
		class NetworkFingerprintData
		{
			internal BitcoinDWaiter waiter;
			internal FingerprintDistribution Distribution;
			internal FingerprintDistribution DefaultDistribution;
			internal Queue<FingerprintDistribution> BlockDistributions = new Queue<FingerprintDistribution>();
		}

		private readonly EventAggregator eventAggregator;
		private readonly Dictionary<NBXplorerNetwork, NetworkFingerprintData> data = new Dictionary<NBXplorerNetwork, NetworkFingerprintData>();
		IDisposable subscription;
		public FingerprintHostedService(EventAggregator eventAggregator,
										NBXplorerNetworkProvider networkProvider,
										BitcoinDWaiters waiters)
		{
			this.eventAggregator = eventAggregator;
			foreach (var network in networkProvider.GetAll())
			{
				data.Add(network, new NetworkFingerprintData()
				{
					waiter = waiters.GetWaiter(network),
					DefaultDistribution = network.CryptoCode == "BTC" ? _DefaultBTC : null
				});
			}
		}
		public Task StartAsync(CancellationToken cancellationToken)
		{
			subscription = this.eventAggregator.Subscribe<RawBlockEvent>(evt =>
			{
				var d = data[evt.Network];
				// If we catchup lot's of old block we do not care about their
				// distribution.
				if (d.waiter.State != BitcoinDWaiterState.Ready)
					return;
				var blockDistribution = FingerprintDistribution.Calculate(evt.Block);
				lock (d)
				{
					d.BlockDistributions.Enqueue(blockDistribution);
					d.Distribution += blockDistribution;
					if (d.BlockDistributions.Count > BlockWindow)
					{
						d.Distribution -= d.BlockDistributions.Dequeue();
					}
				}
			});
			return Task.CompletedTask;
		}

		public FingerprintDistribution GetDistribution(NBXplorerNetwork network)
		{
			return data[network].Distribution ?? data[network].DefaultDistribution;
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			subscription?.Dispose();
			return Task.CompletedTask;
		}

		// Generated via test GenerateDefaultDistribution
		static FingerprintDistribution _DefaultBTC = new FingerprintDistribution(new Dictionary<Fingerprint, int>()
		{
			{ Fingerprint.V1 | Fingerprint.SpendFromP2PKH | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 602 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2PKH | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 432 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHP2WSH | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 319 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHP2WSH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 173 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.FeeSniping | Fingerprint.SequenceAllMinus1, 129 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHP2WSH | Fingerprint.HasWitness | Fingerprint.SequenceAllFinal, 84 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHP2WSH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.SequenceAllFinal, 69 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 62 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2PKH | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 62 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2PKH | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 58 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 55 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 40 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 34 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2PKH | Fingerprint.LowR | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 30 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2WSH | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 30 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2WPKH | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 29 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 26 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2PKH | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 23 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2PKH | Fingerprint.FeeSniping | Fingerprint.SequenceAllMinus1, 20 },
			{ Fingerprint.V1 | Fingerprint.SpendFromMixed | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 20 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2WSH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 19 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 16 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 16 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2WPKH | Fingerprint.HasWitness | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 15 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.RBF, 15 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 15 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.FeeSniping | Fingerprint.SequenceAllMinus1, 13 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2WPKH | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 12 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.FeeSniping | Fingerprint.SequenceAllMinus1, 11 },
			{ Fingerprint.V1 | Fingerprint.SpendFromMixed | Fingerprint.HasWitness | Fingerprint.SequenceAllFinal, 11 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHLegacy | Fingerprint.TimelockZero | Fingerprint.RBF, 11 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2WSH | Fingerprint.HasWitness | Fingerprint.SequenceAllFinal, 10 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHLegacy | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 9 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2SHP2WSH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 9 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2SHLegacy | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllMinus1, 9 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2PKH | Fingerprint.LowR | Fingerprint.FeeSniping | Fingerprint.SequenceAllMinus1, 7 },
			{ Fingerprint.V2 | Fingerprint.SpendFromMixed | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 6 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllMinus1, 6 },
			{ Fingerprint.V2 | Fingerprint.SpendFromMixed | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.FeeSniping | Fingerprint.SequenceAllMinus1, 6 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.RBF, 5 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2SHLegacy | Fingerprint.TimelockZero | Fingerprint.SequenceAllMinus1, 5 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.SequenceAllZero | Fingerprint.RBF, 5 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHLegacy | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.RBF, 5 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 4 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2PKH | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 4 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHLegacy | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 4 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 4 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2WSH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.SequenceAllFinal, 4 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 3 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllZero | Fingerprint.RBF, 3 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHLegacy | Fingerprint.LowR | Fingerprint.SequenceAllFinal, 2 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.SequenceAllMinus1, 2 },
			{ Fingerprint.V2 | Fingerprint.SpendFromMixed | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllMinus1, 2 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHLegacy | Fingerprint.SequenceAllFinal, 2 },
			{ Fingerprint.V1 | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.SequenceAllZero | Fingerprint.RBF, 1 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllZero | Fingerprint.RBF, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllZero | Fingerprint.RBF, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.SequenceAllZero | Fingerprint.RBF, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2WPKH | Fingerprint.HasWitness | Fingerprint.FeeSniping | Fingerprint.SequenceAllMinus1, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2PKH | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceMixed, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2WPKH | Fingerprint.HasWitness | Fingerprint.SequenceAllZero | Fingerprint.RBF, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllZero | Fingerprint.RBF, 1 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2WPKH | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.RBF, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2PKH | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromMixed | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 1 },
			{ Fingerprint.V1 | Fingerprint.SpendFromMixed | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromMixed | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 1 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2PKH | Fingerprint.TimelockZero | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 1 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2PKH | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.RBF, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2PKH | Fingerprint.TimelockZero | Fingerprint.SequenceAllMinus1, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2WSH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.RBF, 1 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHP2WSH | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromMixed | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 1 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2WPKH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.SequenceAllFinal, 1 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2SHP2WPKH | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2WSH | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.TimelockZero | Fingerprint.SequenceAllFinal, 1 },
			{ Fingerprint.V1 | Fingerprint.SpendFromMixed | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 1 },
			{ Fingerprint.V1 | Fingerprint.SpendFromP2PKH | Fingerprint.LowR | Fingerprint.FeeSniping | Fingerprint.SequenceAllMinus1, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2WSH | Fingerprint.HasWitness | Fingerprint.TimelockZero | Fingerprint.RBF, 1 },
			{ Fingerprint.V2 | Fingerprint.SpendFromP2WSH | Fingerprint.HasWitness | Fingerprint.RBF | Fingerprint.SequenceAllMinus2, 1 },
			{ Fingerprint.V1 | Fingerprint.SpendFromMixed | Fingerprint.HasWitness | Fingerprint.LowR | Fingerprint.SequenceAllFinal, 1 },
		});
	}
}
