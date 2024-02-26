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
using NBitcoin.RPC;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using NBXplorer.Logging;
using NBitcoin.Scripting;
using NBXplorer.Backends;

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
			public bool Finished { get; internal set; }
		}

		class ScannedItems
		{
			public Dictionary<Script, KeyPathInformation> KeyPathInformations = new Dictionary<Script, KeyPathInformation>();
			public List<OutputDescriptor> Descriptors = new List<OutputDescriptor>();
		}

		public ScanUTXOSetService(ScanUTXOSetServiceAccessor accessor,
								  IRPCClients rpcClients,
								  KeyPathTemplates keyPathTemplates,
								  IRepositoryProvider repositories)
		{
			accessor.Instance = this;
			RpcClients = rpcClients;
			this.keyPathTemplates = keyPathTemplates;
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
					Status = ScanUTXOStatus.Queued,
					QueuedAt = DateTimeOffset.UtcNow
				},
				Options = options
			};

			var value = _Progress.AddOrUpdate(workItem.Id, workItem, (k, existing) => existing.Finished ? workItem : existing);
			if (value != workItem)
				return false;
			if (!_Channel.Writer.TryWrite(workItem.Id))
			{
				_Progress.TryRemove(workItem.Id, out var unused);
				return false;
			}
			CleanProgressList();
			return true;
		}

		private void CleanProgressList()
		{
			var now = DateTimeOffset.UtcNow;
			List<string> toCleanup = new List<string>();
			foreach (var item in _Progress.Values.Where(p => p.Finished))
			{
				if (now - item.StartTime > TimeSpan.FromHours(24))
					toCleanup.Add(item.Id);
			}
			foreach (var i in toCleanup)
				_Progress.TryRemove(i, out var unused);
		}

		internal Task _Task;
		CancellationTokenSource _Cts = new CancellationTokenSource();
		private readonly KeyPathTemplates keyPathTemplates;

		public IRPCClients RpcClients { get; }
		public IRepositoryProvider Repositories { get; }

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
					if (!_Progress.TryGetValue(item, out var workItem))
					{
						Logs.Explorer.LogError($"{workItem.Network.CryptoCode}: Work has been scheduled for {item}, but the work has not been found in _Progress dictionary. This is likely a bug, contact NBXplorer developers.");
						continue;
					}
					Logs.Explorer.LogInformation($"{workItem.Network.CryptoCode}: Start scanning {workItem.DerivationStrategy.ToPrettyString()} from index {workItem.Options.From} with gap limit {workItem.Options.GapLimit}, batch size {workItem.Options.BatchSize}");
					var rpc = RpcClients.Get(workItem.Network);
					try
					{
						var repo = Repositories.GetRepository(workItem.Network);
						workItem.State.Progress = new ScanUTXOProgress()
						{
							Count = Math.Max(1, Math.Min(workItem.Options.BatchSize, workItem.Options.GapLimit)),
							From = workItem.Options.From,
							StartedAt = DateTimeOffset.UtcNow
						};
						foreach (var feature in keyPathTemplates.GetSupportedDerivationFeatures())
						{
							workItem.State.Progress.HighestKeyIndexFound.Add(feature, null);
						}
						workItem.State.Progress.UpdateRemainingBatches(workItem.Options.GapLimit);
						workItem.State.Status = ScanUTXOStatus.Pending;
						var scannedItems = GetScannedItems(workItem, workItem.State.Progress, workItem.Network);
						var scanning = rpc.StartScanTxoutSetExAsync(new ScanTxoutSetParameters(scannedItems.Descriptors), _Cts.Token);

						while (true)
						{
							var progress = await rpc.GetStatusScanTxoutSetAsync();
							if (progress != null)
							{
								workItem.State.Progress.CurrentBatchProgress = (int)Math.Round(progress.Value);
								workItem.State.Progress.UpdateOverallProgress();
							}
							using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_Cts.Token))
							{
								cts.CancelAfter(TimeSpan.FromSeconds(5.0));
								try
								{
									var result = await scanning.WithCancellation(cts.Token);
									var outputs = result.Outputs;
									if (repo.MinUtxoValue != null)
									{
										outputs = outputs
													.Where(o => o.Coin.Amount >= repo.MinUtxoValue)
													.ToArray();
									}
									var progressObj = workItem.State.Progress.Clone();
									progressObj.BatchNumber++;
									progressObj.From += progressObj.Count;
									progressObj.Found += outputs.Length;
									progressObj.TotalSearched += scannedItems.Descriptors.Count;
									progressObj.UpdateRemainingBatches(workItem.Options.GapLimit);
									progressObj.UpdateOverallProgress();
									Logs.Explorer.LogInformation($"{workItem.Network.CryptoCode}: Scanning of batch {workItem.State.Progress.BatchNumber} for {workItem.DerivationStrategy.ToPrettyString()} complete with {outputs.Length} UTXOs fetched");
									await UpdateRepository(rpc, workItem.DerivationStrategy, repo, outputs, scannedItems, progressObj);

									if (progressObj.RemainingBatches <= -1)
									{
										progressObj.BatchNumber--;
										progressObj.From -= progressObj.Count;
										progressObj.TotalSizeOfUTXOSet = result.SearchedItems;
										progressObj.CompletedAt = DateTimeOffset.UtcNow;
										progressObj.RemainingBatches = 0;
										progressObj.CurrentBatchProgress = 100;
										progressObj.UpdateRemainingBatches(workItem.Options.GapLimit);
										progressObj.UpdateOverallProgress();
										workItem.State.Progress = progressObj;
										workItem.State.Status = ScanUTXOStatus.Complete;
										Logs.Explorer.LogInformation($"{workItem.Network.CryptoCode}: Scanning {workItem.DerivationStrategy.ToPrettyString()} complete {progressObj.Found} UTXOs found in total");
										break;
									}
									else
									{
										scannedItems = GetScannedItems(workItem, progressObj, workItem.Network);
										workItem.State.Progress = progressObj;
										scanning = rpc.StartScanTxoutSetAsync(new ScanTxoutSetParameters(scannedItems.Descriptors));
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
						var progress = workItem.State.Progress.Clone();
						progress.CompletedAt = DateTimeOffset.UtcNow;
						workItem.State.Progress = progress;
						Logs.Explorer.LogError(ex, $"{workItem.Network.CryptoCode}: Error while scanning {workItem.DerivationStrategy.ToPrettyString()}");
					}
					finally
					{
						try { await rpc.AbortScanTxoutSetAsync(); }
						catch { }
					}
					workItem.Finished = true;
				}
			}
			catch (OperationCanceledException) when (_Cts.IsCancellationRequested)
			{

			}
		}

		private async Task UpdateRepository(RPCClient client, DerivationSchemeTrackedSource trackedSource, IRepository repo, ScanTxoutOutput[] outputs, ScannedItems scannedItems, ScanUTXOProgress progressObj)
		{
			var blockHeaders = await client.GetBlockHeadersAsync(outputs.Select(o => o.Height).Distinct().ToList(), _Cts.Token);

			var data = outputs
				.GroupBy(o => o.Coin.Outpoint.Hash)
				.Select(o => (Coins: o.Select(c => c.Coin).ToList(),
							  BlockHeader: blockHeaders.ByHeight.TryGet(o.First().Height),
							  TxId: o.Select(c => c.Coin.Outpoint.Hash).FirstOrDefault(),
							  KeyPathInformations: o.Select(c => scannedItems.KeyPathInformations[c.Coin.ScriptPubKey]).ToList()))
				.Where(o => o.BlockHeader != null)
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

			await repo.SaveBlocks(blockHeaders.Select(b => b.ToSlimChainedBlock()).ToList());
			await repo.SaveMatches(data.Select(o =>
			{
				var trackedTransaction = repo.CreateTrackedTransaction(trackedSource, new TrackedTransactionKey(o.TxId, o.BlockHeader.Hash, true), o.Coins, ToDictionary(o.KeyPathInformations));
				trackedTransaction.Inserted = now;
				trackedTransaction.FirstSeen = o.BlockHeader.Time;
				return trackedTransaction;
			}).ToArray());
		}
		private static Dictionary<Script, KeyPath> ToDictionary(IEnumerable<KeyPathInformation> knownScriptMapping)
		{
			if (knownScriptMapping == null)
				return null;
			var result = new Dictionary<Script, KeyPath>();
			foreach (var keypathInfo in knownScriptMapping)
			{
				result.TryAdd(keypathInfo.ScriptPubKey, keypathInfo.KeyPath);
			}
			return result;
		}

		private ScannedItems GetScannedItems(ScanUTXOWorkItem workItem, ScanUTXOProgress progress, NBXplorerNetwork network)
		{
			var items = new ScannedItems();
			var derivationStrategy = workItem.DerivationStrategy;
			foreach (var feature in keyPathTemplates.GetSupportedDerivationFeatures())
			{
				var keyPathTemplate = keyPathTemplates.GetKeyPathTemplate(feature);
				var lineDerivation = workItem.DerivationStrategy.DerivationStrategy.GetLineFor(keyPathTemplate);
				Enumerable.Range(progress.From, progress.Count)
						  .Select(index =>
						  {
							  var derivation = lineDerivation.Derive((uint)index);
							  var info = new KeyPathInformation(derivation, derivationStrategy, feature,
								  keyPathTemplate.GetKeyPath(index, false), network);
							  items.Descriptors.Add(OutputDescriptor.NewRaw(info.ScriptPubKey, network.NBitcoinNetwork));
							  items.KeyPathInformations.TryAdd(info.ScriptPubKey, info);
							  return info;
						  }).All(_ => true);
			}
			Logs.Explorer.LogInformation($"{workItem.Network.CryptoCode}: Start scanning batch {progress.BatchNumber} of {workItem.DerivationStrategy.ToPrettyString()} from index {progress.From}");
			return items;
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
