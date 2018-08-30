using NBXplorer.Logging;
using NBXplorer.ModelBinders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.RPC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json.Linq;
using NBXplorer.Events;
using NBXplorer.Configuration;
using System.Net.WebSockets;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Reflection;

namespace NBXplorer.Controllers
{
	[Route("v1")]
	[Authorize]
	public class MainController : Controller
	{
		JsonSerializerSettings _SerializerSettings;
		public MainController(
			RepositoryProvider repositoryProvider,
			ChainProvider chainProvider,
			EventAggregator eventAggregator,
			BitcoinDWaitersAccessor waiters,
			AddressPoolServiceAccessor addressPoolService,
			IOptions<MvcJsonOptions> jsonOptions)
		{
			RepositoryProvider = repositoryProvider;
			ChainProvider = chainProvider;
			_SerializerSettings = jsonOptions.Value.SerializerSettings;
			_EventAggregator = eventAggregator;
			Waiters = waiters.Instance;
			AddressPoolService = addressPoolService.Instance;
		}

		EventAggregator _EventAggregator;

		public BitcoinDWaiters Waiters
		{
			get; set;
		}
		public AddressPoolService AddressPoolService
		{
			get;
		}
		public RepositoryProvider RepositoryProvider
		{
			get;
			private set;
		}
		public ChainProvider ChainProvider
		{
			get; set;
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/fees/{blockCount}")]
		public async Task<GetFeeRateResult> GetFeeRate(int blockCount, string cryptoCode)
		{
			var network = GetNetwork(cryptoCode, true);
			if(!network.SupportEstimatesSmartFee)
			{
				throw new NBXplorerError(400, "fee-estimation-unavailable", $"{cryptoCode} does not support estimatesmartfee").AsException();
			}
			var waiter = Waiters.GetWaiter(network);
			var result = await waiter.RPC.SendCommandAsync("estimatesmartfee", blockCount);
			var obj = (JObject)result.Result;
			var feeRateProperty = obj.Property("feerate");
			var rate = feeRateProperty == null ? (decimal)-1 : obj["feerate"].Value<decimal>();
			if(rate == -1)
				throw new NBXplorerError(400, "fee-estimation-unavailable", $"It is currently impossible to estimate fees, please try again later.").AsException();
			return new GetFeeRateResult()
			{
				FeeRate = new FeeRate(Money.Coins(Math.Round(rate / 1000, 8)), 1),
				BlockCount = obj["blocks"].Value<int>()
			};
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/derivations/{strategy}/addresses/unused")]
		public async Task<KeyPathInformation> GetUnusedAddress(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase strategy, DerivationFeature feature = DerivationFeature.Deposit, int skip = 0, bool reserve = false)
		{
			if(strategy == null)
				throw new ArgumentNullException(nameof(strategy));
			var network = GetNetwork(cryptoCode, false);
			var repository = RepositoryProvider.GetRepository(network);
			try
			{
				var result = await repository.GetUnused(strategy, feature, skip, reserve);
				if(reserve)
				{
					while(result == null && skip < repository.MinPoolSize)
					{
						await repository.RefillAddressPoolIfNeeded(strategy, feature);
						result = await repository.GetUnused(strategy, feature, skip, reserve);
					}
					AddressPoolService.RefillAddressPoolIfNeeded(network, strategy, feature);
				}
				if(result == null)
					throw new NBXplorerError(404, "strategy-not-found", $"This strategy is not tracked, or you tried to skip too much unused addresses").AsException();
				return result;
			}
			catch(NotSupportedException)
			{
				throw new NBXplorerError(400, "derivation-not-supported", $"The derivation scheme {feature} is not supported").AsException();
			}
		}

		[HttpPost]
		[Route("cryptos/{cryptoCode}/derivations/{strategy}/addresses/cancelreservation")]
		public IActionResult CancelReservation(string cryptoCode, [ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase strategy, [FromBody]KeyPath[] keyPaths)
		{
			var network = GetNetwork(cryptoCode, false);
			var repo = RepositoryProvider.GetRepository(network);
			repo.CancelReservation(strategy, keyPaths);
			return Ok();
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/scripts/{script}")]
		public IActionResult GetKeyInformations(string cryptoCode,
			[ModelBinder(BinderType = typeof(ScriptModelBinder))] Script script)
		{
			var network = GetNetwork(cryptoCode, false);
			var repo = RepositoryProvider.GetRepository(network);
			var result = repo.GetKeyInformations(new[] { script })
						   .SelectMany(k => k.Value)
						   .ToArray();
			return Json(result);
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/status")]
		public async Task<IActionResult> GetStatus(string cryptoCode)
		{
			var network = GetNetwork(cryptoCode, false);
			var waiter = Waiters.GetWaiter(network);
			var chain = ChainProvider.GetChain(network);
			var repo = RepositoryProvider.GetRepository(network);
			var now = DateTimeOffset.UtcNow;


			var location = waiter.GetLocation();

			var blockchainInfoAsync = waiter.RPCAvailable ? waiter.RPC.GetBlockchainInfoAsyncEx() : null;
			var networkInfoAsync = waiter.RPCAvailable ? waiter.RPC.GetNetworkInfoAsync() : null;
			repo.Ping();
			var pingAfter = DateTimeOffset.UtcNow;

			GetBlockchainInfoResponse blockchainInfo = blockchainInfoAsync == null ? null : await blockchainInfoAsync;
			GetNetworkInfoResponse networkInfo = networkInfoAsync == null ? null : await networkInfoAsync;
			var status = new StatusResult()
			{
				NetworkType = network.NBitcoinNetwork.NetworkType,
				CryptoCode = network.CryptoCode,
				Version = typeof(MainController).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version,
				SupportedCryptoCodes = Waiters.All().Select(w => w.Network.CryptoCode).ToArray(),
				RepositoryPingTime = (pingAfter - now).TotalSeconds,
				IsFullySynched = true
			};

			if(status.RepositoryPingTime > 30)
			{
				Logs.Explorer.LogWarning($"Repository ping exceeded 30 seconds ({(int)status.RepositoryPingTime}), please report the issue to NBXplorer developers");
			}

			if(blockchainInfo != null)
			{
				status.BitcoinStatus = new BitcoinStatus()
				{
					IsSynched = !waiter.IsSynchingCore(blockchainInfo),
					Blocks = (int)blockchainInfo.Blocks,
					Headers = (int)blockchainInfo.Headers,
					VerificationProgress = blockchainInfo.VerificationProgress,
					MinRelayTxFee = new FeeRate(Money.Coins((decimal)networkInfo.relayfee), 1000),
					IncrementalRelayFee = new FeeRate(Money.Coins((decimal)networkInfo.incrementalfee), 1000)
				};
				status.IsFullySynched &= status.BitcoinStatus.IsSynched;
			}
			status.ChainHeight = chain.Height;
			status.SyncHeight = location == null ? (int?)null : chain.FindFork(location).Height;
			status.IsFullySynched &= blockchainInfo != null
									&& waiter.State == BitcoinDWaiterState.Ready
									&& status.SyncHeight.HasValue
									&& blockchainInfo.Headers - status.SyncHeight.Value < 3;
			return Json(status);
		}

		private NBXplorerNetwork GetNetwork(string cryptoCode, bool checkRPC)
		{
			if(cryptoCode == null)
				throw new ArgumentNullException(nameof(cryptoCode));
			cryptoCode = cryptoCode.ToUpperInvariant();
			var network = Waiters.GetWaiter(cryptoCode)?.Network;
			if(network == null)
				throw new NBXplorerException(new NBXplorerError(404, "cryptoCode-not-supported", $"{cryptoCode} is not supported"));

			if(checkRPC)
			{
				var waiter = Waiters.GetWaiter(network);
				if(waiter == null || !waiter.RPCAvailable)
					throw new NBXplorerError(400, "rpc-unavailable", $"The RPC interface is currently not available.").AsException();
			}
			return network;
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/connect")]
		public async Task<IActionResult> ConnectWebSocket(
			string cryptoCode,
			bool includeTransaction = true,
			CancellationToken cancellation = default)
		{
			if(!HttpContext.WebSockets.IsWebSocketRequest)
				return NotFound();

			GetNetwork(cryptoCode, false); // Internally check if cryptoCode is correct

			string listenAllDerivationSchemes = null;
			var listenedBlocks = new ConcurrentDictionary<string, string>();
			var listenedDerivations = new ConcurrentDictionary<(Network, DerivationStrategyBase), DerivationStrategyBase>();

			WebsocketMessageListener server = new WebsocketMessageListener(await HttpContext.WebSockets.AcceptWebSocketAsync(), _SerializerSettings);
			CompositeDisposable subscriptions = new CompositeDisposable();
			subscriptions.Add(_EventAggregator.Subscribe<Events.NewBlockEvent>(async o =>
			{
				if(listenedBlocks.ContainsKey(o.CryptoCode))
				{
					var chain = ChainProvider.GetChain(o.CryptoCode);
					if(chain == null)
						return;
					var block = chain.GetBlock(o.BlockId);
					if(block != null)
					{
						await server.Send(new Models.NewBlockEvent()
						{
							CryptoCode = o.CryptoCode,
							Hash = block.Hash,
							Height = block.Height,
							PreviousBlockHash = block?.Previous
						});
					}
				}
			}));
			subscriptions.Add(_EventAggregator.Subscribe<Events.NewTransactionMatchEvent>(async o =>
			{
				var network = Waiters.GetWaiter(o.CryptoCode);
				if(network == null)
					return;
				if(
				listenAllDerivationSchemes == "*" ||
				listenAllDerivationSchemes == o.CryptoCode ||
				listenedDerivations.ContainsKey((network.Network.NBitcoinNetwork, o.Match.DerivationStrategy)))
				{
					var chain = ChainProvider.GetChain(o.CryptoCode);
					if(chain == null)
						return;
					var blockHeader = o.BlockId == null ? null : chain.GetBlock(o.BlockId);
					await server.Send(new Models.NewTransactionEvent()
					{
						CryptoCode = o.CryptoCode,
						DerivationStrategy = o.Match.DerivationStrategy,
						BlockId = blockHeader?.Hash,
						TransactionData = Utils.ToTransactionResult(includeTransaction, chain, new[] { o.SavedTransaction }),
						Inputs = o.Match.Inputs,
						Outputs = o.Match.Outputs
					});
				}
			}));
			try
			{
				while(server.Socket.State == WebSocketState.Open)
				{
					object message = await server.NextMessageAsync(cancellation);
					switch(message)
					{
						case Models.NewBlockEventRequest r:
							r.CryptoCode = r.CryptoCode ?? cryptoCode;
							listenedBlocks.TryAdd(r.CryptoCode, r.CryptoCode);
							break;
						case Models.NewTransactionEventRequest r:
							if(r.DerivationSchemes != null)
							{
								r.CryptoCode = r.CryptoCode ?? cryptoCode;
								var network = Waiters.GetWaiter(r.CryptoCode)?.Network;
								if(network == null)
									break;
								foreach(var derivation in r.DerivationSchemes)
								{
									var parsed = new DerivationStrategyFactory(network.NBitcoinNetwork).Parse(derivation);
									listenedDerivations.TryAdd((network.NBitcoinNetwork, parsed), parsed);
								}
							}
							else
							{
								listenAllDerivationSchemes = r.CryptoCode;
							}
							break;
						default:
							break;
					}
				}
			}
			catch when(server.Socket.State != WebSocketState.Open)
			{
			}
			finally { subscriptions.Dispose(); await server.DisposeAsync(cancellation); }
			return new EmptyResult();
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/transactions/{txId}")]
		public IActionResult GetTransaction(
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 txId,
			bool includeTransaction = true,
			string cryptoCode = null)
		{
			var network = GetNetwork(cryptoCode, false);
			var chain = this.ChainProvider.GetChain(network);
			var result = RepositoryProvider.GetRepository(network).GetSavedTransactions(txId);
			if(result.Length == 0)
				return NotFound();
			return Json(Utils.ToTransactionResult(includeTransaction, chain, result));
		}

		[HttpPost]
		[Route("cryptos/{cryptoCode}/derivations/{derivationStrategy}")]
		public async Task<IActionResult> TrackWallet(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase derivationStrategy)
		{
			if(derivationStrategy == null)
				return NotFound();
			var network = GetNetwork(cryptoCode, false);
			foreach(var feature in Enum.GetValues(typeof(DerivationFeature)).Cast<DerivationFeature>())
			{
				await RepositoryProvider.GetRepository(network).RefillAddressPoolIfNeeded(derivationStrategy, feature, 1);
				AddressPoolService.RefillAddressPoolIfNeeded(network, derivationStrategy, feature);
			}
			return Ok();
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/derivations/{extPubKey}/transactions")]
		public async Task<GetTransactionsResponse> GetTransactions(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase extPubKey,
			[ModelBinder(BinderType = typeof(BookmarksModelBinding))]
			HashSet<Bookmark> unconfirmedBookmarks = null,
			[ModelBinder(BinderType = typeof(BookmarksModelBinding))]
			HashSet<Bookmark> confirmedBookmarks = null,
			[ModelBinder(BinderType = typeof(BookmarksModelBinding))]
			HashSet<Bookmark> replacedBookmarks = null,
			bool includeTransaction = true,
			bool longPolling = false)
		{
			if(extPubKey == null)
				throw new ArgumentNullException(nameof(extPubKey));
			var network = GetNetwork(cryptoCode, false);
			var chain = ChainProvider.GetChain(network);
			var repo = RepositoryProvider.GetRepository(network);
			var waitingTransaction = longPolling ? WaitingTransaction(extPubKey) : Task.FromResult(false);
			GetTransactionsResponse response = null;
			while(true)
			{
				response = new GetTransactionsResponse();
				int currentHeight = chain.Height;
				response.Height = currentHeight;
				var txs = GetAnnotatedTransactions(repo, chain, extPubKey);
				foreach(var item in new[]
				{
					new
					{
						TxSet = response.ConfirmedTransactions,
						KnownBookmarks = confirmedBookmarks  ?? new HashSet<Bookmark>(),
						AnnotatedTx = txs.ConfirmedTransactions
					},
					new
					{
						TxSet = response.UnconfirmedTransactions,
						KnownBookmarks = unconfirmedBookmarks  ?? new HashSet<Bookmark>(),
						AnnotatedTx = txs.UnconfirmedTransactions
					},
					new
					{
						TxSet = response.ReplacedTransactions,
						KnownBookmarks = replacedBookmarks  ?? new HashSet<Bookmark>(),
						AnnotatedTx = txs.ReplacedTransactions
					},
				})
				{
					item.TxSet.Bookmark = Bookmark.Start;
					item.TxSet.KnownBookmark = item.KnownBookmarks.Contains(Bookmark.Start) ? Bookmark.Start : null;

					BookmarkProcessor processor = new BookmarkProcessor(32 + 32 + 25);
					foreach(var tx in item.AnnotatedTx.Values)
					{
						processor.PushNew();
						processor.AddData(tx.Record.Transaction.GetHash());
						processor.AddData(tx.Record.BlockHash ?? uint256.Zero);
						processor.UpdateBookmark();

						var txInfo = new TransactionInformation()
						{
							BlockHash = tx.Record.BlockHash,
							Height = tx.Record.BlockHash == null ? null : tx.Height,
							TransactionId = tx.Record.Transaction.GetHash(),
							Transaction = includeTransaction ? tx.Record.Transaction : null,
							Confirmations = tx.Record.BlockHash == null ? 0 : currentHeight - tx.Height.Value + 1,
							Timestamp = txs.GetByTxId(tx.Record.Transaction.GetHash()).Select(t => t.Record.FirstSeen).First(),
							Inputs = ToMatch(txs, tx.Record.Transaction.Inputs.Select(o => txs.GetUTXO(o.PrevOut)).ToList(), extPubKey, tx.Record.TransactionMatch.Inputs),
							Outputs = ToMatch(txs, tx.Record.Transaction.Outputs, extPubKey, tx.Record.TransactionMatch.Outputs)
						};

						item.TxSet.Transactions.Add(txInfo);

						txInfo.BalanceChange = txInfo.Outputs.Select(o => o.Value).Sum() - txInfo.Inputs.Select(o => o.Value).Sum();

						item.TxSet.Bookmark = processor.CurrentBookmark;
						if(item.KnownBookmarks.Contains(processor.CurrentBookmark))
						{
							item.TxSet.KnownBookmark = processor.CurrentBookmark;
							item.TxSet.Transactions.Clear();
						}
					}
				}

				if(response.HasChanges() || !(await waitingTransaction))
					break;
				waitingTransaction = Task.FromResult(false); //next time, will not wait
			}

			return response;
		}

		List<TransactionInformationMatch> ToMatch(AnnotatedTransactionCollection txs,
												 List<TxOut> outputs,
												 DerivationStrategyBase derivation,
												 Repository.TransactionMiniKeyInformation[] keyInformations)
		{
			var result = new List<TransactionInformationMatch>();
			for(int i = 0; i < outputs.Count; i++)
			{
				if(outputs[i] == null)
					continue;
				var keyPath = txs.GetKeyPath(outputs[i].ScriptPubKey);
				if(keyPath == null)
					continue;

				result.Add(new TransactionInformationMatch() { Index = i, KeyPath = keyPath, Value = outputs[i].Value });
			}
			return result;
		}

		[HttpPost]
		[Route("cryptos/{cryptoCode}/rescan")]
		public async Task<IActionResult> Rescan(string cryptoCode, [FromBody]RescanRequest rescanRequest)
		{
			if(rescanRequest == null)
				throw new ArgumentNullException(nameof(rescanRequest));
			if(rescanRequest?.Transactions == null)
				throw new NBXplorerException(new NBXplorerError(400, "transactions-missing", "You must specify 'transactions'"));

			bool willFetchTransactions = rescanRequest.Transactions.Any(t => t.Transaction == null);
			bool needTxIndex = rescanRequest.Transactions.Any(t => t.Transaction == null && t.BlockId == null);
			var network = GetNetwork(cryptoCode, willFetchTransactions);

			var rpc = Waiters.GetWaiter(cryptoCode).RPC.PrepareBatch();
			var repo = RepositoryProvider.GetRepository(network);

			var fetchingTransactions = rescanRequest
				.Transactions
				.Select(t => FetchTransaction(rpc, t))
				.ToArray();

			await rpc.SendBatchAsync();
			await Task.WhenAll(fetchingTransactions);

			var transactions = fetchingTransactions.Select(t => t.GetAwaiter().GetResult())
												   .Where(tx => tx.Transaction != null)
												   .ToArray();

			foreach(var txs in transactions.GroupBy(t => t.BlockId, t => (t.Transaction, t.BlockTime))
											.OrderBy(t => t.First().BlockTime))
			{
				repo.SaveTransactions(txs.First().BlockTime, txs.Select(t => t.Transaction).ToArray(), txs.Key);
				foreach(var tx in txs)
				{
					var matches = repo.GetMatches(tx.Transaction).Select(m => new MatchedTransaction() { BlockId = txs.Key, Match = m }).ToArray();
					repo.SaveMatches(tx.BlockTime, matches);
					AddressPoolService.RefillAddressPoolIfNeeded(network, matches);
				}
			}

			return Ok();
		}

		async Task<(uint256 BlockId, Transaction Transaction, DateTimeOffset BlockTime)> FetchTransaction(RPCClient rpc, RescanRequest.TransactionToRescan transaction)
		{
			if(transaction.Transaction != null)
			{
				if(transaction.BlockId == null)
					throw new NBXplorerException(new NBXplorerError(400, "block-id-missing", "You must specify 'transactions[].blockId' if you specified 'transactions[].transaction'"));
				var blockTime = await rpc.GetBlockTimeAsync(transaction.BlockId, false);
				if(blockTime == null)
					return (null, null, default);
				return (transaction.BlockId, transaction.Transaction, blockTime.Value);
			}
			else if(transaction.TransactionId != null)
			{
				if(transaction.BlockId != null)
				{
					var getTx = rpc.GetRawTransactionAsync(transaction.TransactionId, transaction.BlockId, false);
					var blockTime = await rpc.GetBlockTimeAsync(transaction.BlockId, false);
					if(blockTime == null)
						return (null, null, default);
					return (transaction.BlockId, await getTx, blockTime.Value);
				}
				else
				{
					try
					{
						var txInfo = await rpc.GetRawTransactionInfoAsync(transaction.TransactionId);
						return (txInfo.BlockHash, txInfo.Transaction, txInfo.BlockTime.Value);
					}
					catch(RPCException ex) when(ex.RPCCode == RPCErrorCode.RPC_INVALID_ADDRESS_OR_KEY)
					{
						return (null, null, default);
					}
				}
			}
			else
			{
				throw new NBXplorerException(new NBXplorerError(400, "transaction-id-missing", "You must specify 'transactions[].transactionId' or 'transactions[].transaction'"));
			}
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/derivations/{extPubKey}/utxos")]
		public async Task<UTXOChanges> GetUTXOs(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase extPubKey,
			[ModelBinder(BinderType = typeof(BookmarksModelBinding))]
			HashSet<Bookmark> confirmedBookmarks = null,
			[ModelBinder(BinderType = typeof(BookmarksModelBinding))]
			HashSet<Bookmark> unconfirmedBookmarks = null,
			bool longPolling = false)
		{
			unconfirmedBookmarks = unconfirmedBookmarks ?? new HashSet<Bookmark>();
			confirmedBookmarks = confirmedBookmarks ?? new HashSet<Bookmark>();
			if(extPubKey == null)
				throw new ArgumentNullException(nameof(extPubKey));
			var network = GetNetwork(cryptoCode, false);
			var chain = ChainProvider.GetChain(network);
			var repo = RepositoryProvider.GetRepository(network);
			var waitingTransaction = longPolling ? WaitingTransaction(extPubKey) : Task.FromResult(false);
			UTXOChanges changes = null;

			while(true)
			{
				changes = new UTXOChanges();
				changes.CurrentHeight = chain.Height;
				var transactions = GetAnnotatedTransactions(repo, chain, extPubKey);
				Func<Script[], bool[]> matchScript = (scripts) => scripts.Select(s => transactions.GetKeyPath(s) != null).ToArray();

				var states = UTXOStateResult.CreateStates(matchScript,
														unconfirmedBookmarks,
														transactions.UnconfirmedTransactions.Values.Select(c => c.Record.Transaction),
														confirmedBookmarks,
														transactions.ConfirmedTransactions.Values.Select(c => c.Record.Transaction));

				changes.Confirmed = SetUTXOChange(states.Confirmed);
				changes.Unconfirmed = SetUTXOChange(states.Unconfirmed, states.Confirmed.Actual);



				FillUTXOsInformation(changes.Confirmed.UTXOs, transactions, changes.CurrentHeight);
				FillUTXOsInformation(changes.Unconfirmed.UTXOs, transactions, changes.CurrentHeight);

				if(changes.HasChanges || !(await waitingTransaction))
					break;
				waitingTransaction = Task.FromResult(false); //next time, will not wait
			}
			changes.DerivationStrategy = extPubKey;
			return changes;
		}

		private void CleanConflicts(Repository repo, DerivationStrategyBase extPubKey, AnnotatedTransactionCollection transactions)
		{
			var cleaned = transactions.DuplicatedTransactions.Where(c => (DateTimeOffset.UtcNow - c.Record.Inserted) > TimeSpan.FromDays(1.0)).Select(c => c.Record).ToArray();
			if(cleaned.Length != 0)
			{
				foreach(var tx in cleaned)
				{
					_EventAggregator.Publish(new EvictedTransactionEvent(tx.Transaction.GetHash()));
				}
				repo.CleanTransactions(extPubKey, cleaned.ToList());
			}
		}

		static int[] MaxValue = new[] { int.MaxValue };
		private void FillUTXOsInformation(List<UTXO> utxos, AnnotatedTransactionCollection transactions, int currentHeight)
		{
			for(int i = 0; i < utxos.Count; i++)
			{
				var utxo = utxos[i];
				utxo.KeyPath = transactions.GetKeyPath(utxo.ScriptPubKey);
				utxo.Feature = DerivationStrategyBase.GetFeature(utxo.KeyPath);
				var txHeight = transactions.GetByTxId(utxo.Outpoint.Hash)
									.Select(t => t.Height)
									.Where(h => h.HasValue)
									.Select(t => t.Value)
									.Concat(MaxValue)
									.Min();
				var firstSeen = transactions
					.GetByTxId(utxo.Outpoint.Hash)
					.Select(o => o.Record.FirstSeen)
					.FirstOrDefault();
				var isUnconf = txHeight == MaxValue[0];
				utxo.Confirmations = isUnconf ? 0 : currentHeight - txHeight + 1;
				utxo.Timestamp = firstSeen;
			}
		}

		private UTXOChange SetUTXOChange(UTXOStates states, UTXOState substract = null)
		{
			substract = substract ?? new UTXOState();
			var substractedSpent = new HashSet<OutPoint>(substract.SpentUTXOs);
			var substractedReceived = new HashSet<OutPoint>(substract.UTXOByOutpoint.Select(u => u.Key));

			UTXOChange change = new UTXOChange();
			change.KnownBookmark = states.Known == null ? null : states.Known.CurrentBookmark;
			change.Bookmark = states.Actual.CurrentBookmark;

			states.Known = states.Known ?? new UTXOState();

			foreach(var coin in states.Actual.UTXOByOutpoint)
			{
				if(!states.Known.UTXOByOutpoint.ContainsKey(coin.Key) &&
					!substractedReceived.Contains(coin.Key))
					change.UTXOs.Add(new UTXO(coin.Value));
			}

			foreach(var outpoint in states.Actual.SpentUTXOs)
			{
				if(!states.Known.SpentUTXOs.Contains(outpoint) &&
					(states.Known.UTXOByOutpoint.ContainsKey(outpoint) || substractedReceived.Contains(outpoint)) &&
					!substractedSpent.Contains(outpoint))
					change.SpentOutpoints.Add(outpoint);
			}
			return change;
		}

		private AnnotatedTransactionCollection GetAnnotatedTransactions(Repository repo, SlimChain chain, DerivationStrategyBase extPubKey)
		{
			var annotatedTransactions = new AnnotatedTransactionCollection(repo
				.GetTransactions(extPubKey)
				.Select(t => new AnnotatedTransaction(t, chain))
				.ToList());
			CleanConflicts(repo, extPubKey, annotatedTransactions);
			return annotatedTransactions;
		}

		private async Task<bool> WaitingTransaction(DerivationStrategyBase extPubKey)
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			cts.CancelAfter(10000);

			try
			{
				await _EventAggregator.WaitNext<NewTransactionMatchEvent>(e => e.Match.DerivationStrategy.ToString() == extPubKey.ToString(), cts.Token);
				return true;
			}
			catch(OperationCanceledException) { return false; }
		}

		[HttpPost]
		[Route("cryptos/{cryptoCode}/transactions")]
		public async Task<BroadcastResult> Broadcast(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase extPubKey)
		{
			var network = GetNetwork(cryptoCode, true);

			var tx = network.NBitcoinNetwork.Consensus.ConsensusFactory.CreateTransaction();
			var stream = new BitcoinStream(Request.Body, false);
			tx.ReadWrite(stream);

			var waiter = this.Waiters.GetWaiter(network);
			var repo = RepositoryProvider.GetRepository(network);
			var chain = ChainProvider.GetChain(network);
			RPCException rpcEx = null;
			try
			{
				await waiter.RPC.SendRawTransactionAsync(tx);
				return new BroadcastResult(true);
			}
			catch(RPCException ex)
			{
				rpcEx = ex;
				Logs.Explorer.LogInformation($"{network.CryptoCode}: Transaction {tx.GetHash()} failed to broadcast (Code: {ex.RPCCode}, Message: {ex.RPCCodeMessage}, Details: {ex.Message} )");
				if(extPubKey != null && ex.Message.StartsWith("Missing inputs", StringComparison.OrdinalIgnoreCase))
				{
					Logs.Explorer.LogInformation($"{network.CryptoCode}: Trying to broadcast unconfirmed of the wallet");
					var transactions = GetAnnotatedTransactions(repo, chain, extPubKey);
					foreach(var existing in transactions.UnconfirmedTransactions.Values)
					{
						try
						{
							await waiter.RPC.SendRawTransactionAsync(existing.Record.Transaction);
						}
						catch { }
					}

					try
					{
						await waiter.RPC.SendRawTransactionAsync(tx);
						Logs.Explorer.LogInformation($"{network.CryptoCode}: Broadcast success");
						return new BroadcastResult(true);
					}
					catch(RPCException)
					{
						Logs.Explorer.LogInformation($"{network.CryptoCode}: Transaction {tx.GetHash()} failed to broadcast (Code: {ex.RPCCode}, Message: {ex.RPCCodeMessage}, Details: {ex.Message} )");
					}
				}
				return new BroadcastResult(false)
				{
					RPCCode = rpcEx.RPCCode,
					RPCCodeMessage = rpcEx.RPCCodeMessage,
					RPCMessage = rpcEx.Message
				};
			}
		}
	}
}
