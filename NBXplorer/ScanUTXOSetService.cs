using System;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.RPC;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using NBXplorer.Logging;

namespace NBXplorer
{
	/// <summary>
	/// Hack, ASP.NET core DI does not support having one singleton for multiple interfaces
	/// </summary>
	public class ScanUTXOSetServiceAccessor
	{
		public ScanUTXOSetService Instance
		{
			get; set;
		}
	}

	public class ScanUTXOSetOptions
	{
		public int GapLimit { get; set; } = 10_000;
		public int BatchSize { get; set; } = 1000;
		public DerivationFeature[] DerivationFeatures { get; set; } = new[] { DerivationFeature.Change, DerivationFeature.Deposit, DerivationFeature.Direct };
		public int From { get; set; }
	}
	public class ScanUTXOSetService : IHostedService
	{
		class ScanUTXOWorkItem
		{
			public ScanUTXOWorkItem(NBXplorerNetwork network,
									DerivationStrategyBase derivationStrategy)
			{
				Network = network;
				DerivationStrategy = new DerivationSchemeTrackedSource(derivationStrategy);
				Id = DerivationStrategy.ToString();
				StartTime = DateTime.UtcNow;
			}
			public string Id { get; set; }
			public DateTimeOffset StartTime { get; set; }
			public ScanUTXOSetOptions Options { get; set; }
			public NBXplorerNetwork Network { get; }
			public DerivationSchemeTrackedSource DerivationStrategy { get; set; }
			public ScanUTXOInformation State { get; set; }
		}

		class ScannedItems
		{
			public Dictionary<Script, KeyPathInformation> KeyPathInformations = new Dictionary<Script, KeyPathInformation>();
			public List<ScanTxoutSetObject> Descriptors = new List<ScanTxoutSetObject>();
		}

		public ScanUTXOSetService(ScanUTXOSetServiceAccessor accessor,
								  RPCClientProvider rpcClients,
								  ChainProvider chains,
								  RepositoryProvider repositories)
		{
			accessor.Instance = this;
			RpcClients = rpcClients;
			Chains = chains;
			Repositories = repositories;
		}
		Channel<string> _Channel = Channel.CreateBounded<string>(500);
		ConcurrentDictionary<string, ScanUTXOWorkItem> _Progress = new ConcurrentDictionary<string, ScanUTXOWorkItem>();

		internal bool EnqueueScan(NBXplorerNetwork network, DerivationStrategyBase derivationScheme, ScanUTXOSetOptions options)
		{
			var workItem = new ScanUTXOWorkItem(network, derivationScheme)
			{
				State = new ScanUTXOInformation()
				{
					Status = ScanUTXOStatus.Queued
				},
				Options = options
			};

			var value = _Progress.AddOrUpdate(workItem.Id, workItem, (k, existing) => existing.State.Status == ScanUTXOStatus.Complete || existing.State.Status == ScanUTXOStatus.Error ? workItem : existing);
			if (value != workItem)
				return false;
			if (!_Channel.Writer.TryWrite(workItem.Id))
			{
				_Progress.TryRemove(workItem.Id, out var unused);
				return false;
			}
			CleanProgressList(workItem);
			return true;
		}

		private void CleanProgressList(ScanUTXOWorkItem workItem)
		{
			var now = DateTimeOffset.UtcNow;
			List<string> toCleanup = new List<string>();
			foreach (var item in _Progress.Values)
			{
				if (now - item.StartTime > TimeSpan.FromHours(1))
					toCleanup.Add(workItem.Id);
			}
			foreach (var i in toCleanup)
				_Progress.TryRemove(i, out var unused);
		}

		internal 

		Task _Task;
		CancellationTokenSource _Cts = new CancellationTokenSource();
		public RPCClientProvider RpcClients { get; }
		public ChainProvider Chains { get; }
		public RepositoryProvider Repositories { get; }

		public Task StartAsync(CancellationToken cancellationToken)
		{
			_Task = Listen();
			return Task.CompletedTask;
		}

		private async Task Listen()
		{
			try
			{
				while (await _Channel.Reader.WaitToReadAsync(_Cts.Token) && _Channel.Reader.TryRead(out var item))
				{
					var workItem = _Progress[item];
					Logs.Explorer.LogInformation($"{workItem.Network.CryptoCode}: Start scanning {workItem.DerivationStrategy.ToPrettyString()} from index {workItem.Options.From} with gap limit {workItem.Options.GapLimit}, batch size {workItem.Options.BatchSize}");
					var rpc = RpcClients.GetRPCClient(workItem.Network);
					var chain = Chains.GetChain(workItem.Network);
					try
					{
						var repo = Repositories.GetRepository(workItem.Network);
						workItem.State.Progress = new ScanUTXOProgress()
						{
							Count = Math.Min(workItem.Options.BatchSize, workItem.Options.GapLimit),
							From = workItem.Options.From
						};
						foreach (var feature in workItem.Options.DerivationFeatures)
						{
							workItem.State.Progress.HighestKeyIndexFound.Add(feature, null);
						}
						workItem.State.Status = ScanUTXOStatus.Pending;
						var scannedItems = GetScannedItems(workItem, workItem.State.Progress);
						var scanning = rpc.StartScanTxoutSetAsync(scannedItems.Descriptors.ToArray());

						while (true)
						{
							var progress = await rpc.GetStatusScanTxoutSetAsync();
							if (progress != null)
								workItem.State.Progress.CurrentBatchProgress = (double)progress.Value;
							using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_Cts.Token))
							{
								cts.CancelAfter(TimeSpan.FromSeconds(5.0));
								try
								{
									var result = await scanning.WithCancellation(cts.Token);
									var progressObj = workItem.State.Progress.Clone();
									progressObj.BatchNumber++;
									progressObj.Found += result.Outputs.Length;
									progressObj.From += progressObj.Count;
									progressObj.TotalSearched += scannedItems.Descriptors.Count;
									Logs.Explorer.LogInformation($"{workItem.Network.CryptoCode}: Scanning of batch {workItem.State.Progress.BatchNumber} for {workItem.DerivationStrategy.ToPrettyString()} complete with {result.Outputs.Length} UTXOs fetched");
									await UpdateRepository(workItem.DerivationStrategy, repo, chain, result.Outputs, scannedItems, progressObj);

									scannedItems = GetScannedItems(workItem, progressObj);
									if (scannedItems.Descriptors.Count == 0)
									{
										workItem.State.Status = ScanUTXOStatus.Complete;
										progressObj = workItem.State.Progress.Clone();
										progressObj.CurrentBatchProgress = 100.0;
										workItem.State.Progress = progressObj;
										Logs.Explorer.LogInformation($"{workItem.Network.CryptoCode}: Scanning {workItem.DerivationStrategy.ToPrettyString()} complete");
										break;
									}
									else
									{
										workItem.State.Progress = progressObj;
										scanning = rpc.StartScanTxoutSetAsync(scannedItems.Descriptors.ToArray());
									}
								}
								catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
								{

								}
							}
						}
					}
					catch (Exception ex) when (!_Cts.IsCancellationRequested)
					{
						workItem.State.Status = ScanUTXOStatus.Error;
						workItem.State.Error = ex.Message;
						Logs.Explorer.LogError(ex, $"{workItem.Network.CryptoCode}: Error while scanning {workItem.DerivationStrategy.ToPrettyString()}");
					}
					finally
					{
						await rpc.AbortScanTxoutSetAsync();
					}
				}
			}
			catch (OperationCanceledException) when (_Cts.IsCancellationRequested)
			{

			}
		}

		private async Task UpdateRepository(DerivationSchemeTrackedSource trackedSource, Repository repo, SlimChain chain, ScanTxoutOutput[] outputs, ScannedItems scannedItems, ScanUTXOProgress progressObj)
		{
			var data = outputs
				.GroupBy(o => o.Coin.Outpoint.Hash)
				.Select(o => (Coins: o.Select(c => c.Coin).ToList(),
							  BlockId: chain.GetBlock(o.First().Height)?.Hash,
							  TxId: o.Select(c => c.Coin.Outpoint.Hash).FirstOrDefault(),
							  KeyPathInformations: o.Select(c => scannedItems.KeyPathInformations[c.Coin.ScriptPubKey]).ToList()))
				.Where(o => o.BlockId != null)
				.Select(o =>
				{
					foreach (var keyInfo in o.KeyPathInformations)
					{
						var index = keyInfo.KeyPath.Indexes.Last();
						var highest = progressObj.HighestKeyIndexFound[keyInfo.Feature];
						if (highest == null || index > highest.Value)
						{
							progressObj.HighestKeyIndexFound[keyInfo.Feature] = (int)index;
						}
					}
					return o;
				}).ToList();

			await repo.SaveKeyInformations(scannedItems.
				KeyPathInformations.
				Select(p => p.Value).
				Where(p =>
				{
					var highest = progressObj.HighestKeyIndexFound[p.Feature];
					if (highest == null)
						return false;
					return p.KeyPath.Indexes.Last() <= highest.Value;
				}).ToArray());

			await repo.UpdateAddressPool(trackedSource, progressObj.HighestKeyIndexFound);

			DateTimeOffset now = DateTimeOffset.UtcNow;
			await repo.SaveMatches(data.Select(o => new MatchedTransaction()
			{
				TrackedSource = trackedSource,
				TrackedTransaction = new TrackedTransaction(new TrackedTransactionKey(o.TxId, o.BlockId, true), o.Coins, o.KeyPathInformations)
				{
					FirstSeen = now,
					Inserted = now,
				}
			}).ToArray());
		}

		private ScannedItems GetScannedItems(ScanUTXOWorkItem workItem, ScanUTXOProgress progress)
		{
			var items = new ScannedItems();
			if (workItem.Options.DerivationFeatures
				.All(f => PastGapLimit(progress.HighestKeyIndexFound[f], progress.From, workItem.Options.GapLimit)))
				return items;

			var derivationStrategy = workItem.DerivationStrategy;
			foreach (var feature in workItem.Options.DerivationFeatures)
			{
				var path = DerivationStrategyBase.GetKeyPath(feature);
				var lineDerivation = workItem.DerivationStrategy.DerivationStrategy.GetLineFor(feature);
				Enumerable.Range(progress.From, progress.Count)
						  .Select(index =>
						  {
							  var derivation = lineDerivation.Derive((uint)index);
							  var info = new KeyPathInformation()
							  {
								  ScriptPubKey = derivation.ScriptPubKey,
								  Redeem = derivation.Redeem,
								  TrackedSource = derivationStrategy,
								  DerivationStrategy = derivationStrategy.DerivationStrategy,
								  Feature = feature,
								  KeyPath = path.Derive(index, false)
							  };
							  items.Descriptors.Add(new ScanTxoutSetObject(ScanTxoutDescriptor.Raw(info.ScriptPubKey)));
							  items.KeyPathInformations.TryAdd(info.ScriptPubKey, info);
							  return info;
						  }).All(_ => true);
			}
			Logs.Explorer.LogInformation($"{workItem.Network.CryptoCode}: Start scanning batch {progress.BatchNumber} of {workItem.DerivationStrategy.ToPrettyString()} from index {progress.From}");
			return items;
		}

		private static bool PastGapLimit(int? highestIndex, int currentIndex, int gapLimit)
		{
			// If gap limit is 100, current index is 100, highestIndex is null (-1), then we are past the gap limit
			highestIndex = highestIndex ?? -1;
			// If gap limit is 100, current index is 300, highestIndex is 200, then we are not past the gap limit
			return currentIndex - highestIndex.Value > gapLimit;
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			_Cts.Cancel();
			_Channel.Writer.Complete();
			return _Task;
		}

		public ScanUTXOInformation GetInformation(NBXplorerNetwork network, DerivationStrategyBase derivationScheme)
		{
			_Progress.TryGetValue(new ScanUTXOWorkItem(network, derivationScheme).Id, out var workItem);
			return workItem?.State;
		}
	}
}
