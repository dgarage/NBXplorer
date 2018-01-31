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
			IOptions<MvcJsonOptions> jsonOptions)
		{
			RepositoryProvider = repositoryProvider;
			ChainProvider = chainProvider;
			_SerializerSettings = jsonOptions.Value.SerializerSettings;
			_EventAggregator = eventAggregator;
			Waiters = waiters.Instance;
		}

		EventAggregator _EventAggregator;

		public BitcoinDWaiters Waiters
		{
			get; set;
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
			var network = GetNetwork(cryptoCode);
			var waiter = GetWaiter(network);
			var result = await waiter.RPC.SendCommandAsync("estimatesmartfee", blockCount);
			var feeRateProperty = ((JObject)result.Result).Property("feeRate");
			var rate = feeRateProperty == null ? (decimal)-1 : ((JObject)result.Result)["feerate"].Value<decimal>();
			if(rate == -1)
				throw new NBXplorerError(400, "fee-estimation-unavailable", $"It is currently impossible to estimate fees, please try again later.").AsException();
			return new GetFeeRateResult()
			{
				FeeRate = new FeeRate(Money.Coins(rate), 1000),
				BlockCount = ((JObject)result.Result)["blocks"].Value<int>()
			};
		}

		private BitcoinDWaiter GetWaiter(NBXplorerNetwork network)
		{
			var waiter = Waiters.GetWaiter(network);
			if(!waiter.RPCAvailable)
				throw RPCUnavailable();
			return waiter;
		}

		private static NBXplorerException RPCUnavailable()
		{
			return new NBXplorerError(400, "rpc-unavailable", $"The RPC interface is currently not available.").AsException();
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
			var network = GetNetwork(cryptoCode);
			var repository = RepositoryProvider.GetRepository(network);
			try
			{
				var result = await repository.GetUnused(strategy, feature, skip, reserve);
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
			var network = GetNetwork(cryptoCode);
			var repo = RepositoryProvider.GetRepository(network);
			repo.CancelReservation(strategy, keyPaths);
			return Ok();
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/status")]
		public async Task<IActionResult> GetStatus(string cryptoCode)
		{
			var network = GetNetwork(cryptoCode);
			var waiter = Waiters.GetWaiter(network);
			var chain = ChainProvider.GetChain(network);
			var repo = RepositoryProvider.GetRepository(network);
			var now = DateTimeOffset.UtcNow;


			var location = waiter.GetLocation();

			var blockchainInfoAsync = waiter.RPCAvailable ? waiter.RPC.GetBlockchainInfoAsync() : null;
			repo.Ping();
			var pingAfter = DateTimeOffset.UtcNow;
			GetBlockchainInfoResponse blockchainInfo = blockchainInfoAsync == null ? null : await blockchainInfoAsync;
			var status = new StatusResult()
			{
				ChainType = network.DefaultSettings.ChainType,
				CryptoCode = network.CryptoCode,
				Version = typeof(MainController).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version,
				SupportedCryptoCodes = Waiters.All().Select(w => w.Network.CryptoCode).ToArray(),
				RepositoryPingTime = (DateTimeOffset.UtcNow - now).TotalSeconds,
				IsFullySynched = true
			};

			if(blockchainInfo != null)
			{
				status.BitcoinStatus = new BitcoinStatus()
				{
					IsSynched = !waiter.IsSynchingCore(blockchainInfo),
					Blocks = blockchainInfo.Blocks,
					Headers = blockchainInfo.Headers,
					VerificationProgress = blockchainInfo.VerificationProgress
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

		private NBXplorerNetwork GetNetwork(string cryptoCode)
		{
			if(cryptoCode == null)
				throw new ArgumentNullException(nameof(cryptoCode));
			cryptoCode = cryptoCode.ToUpperInvariant();
			var network = Waiters.GetWaiter(cryptoCode)?.Network;
			if(network == null)
				throw new NBXplorerException(new NBXplorerError(404, "cryptoCode-not-supported", $"{cryptoCode} is not supported"));
			return network;
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/connect")]
		public async Task<IActionResult> ConnectWebSocket(
			string cryptoCode,
			bool includeTransaction = true,
			CancellationToken cancellation = default(CancellationToken))
		{
			if(!HttpContext.WebSockets.IsWebSocketRequest)
				return NotFound();

			GetNetwork(cryptoCode); // Internally check if cryptoCode is correct
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
							Hash = block.HashBlock,
							Height = block.Height,
							PreviousBlockHash = block?.Previous.HashBlock
						});
					}
				}
			}));
			subscriptions.Add(_EventAggregator.Subscribe<Events.NewTransactionMatchEvent>(async o =>
			{
				var network = Waiters.GetWaiter(o.CryptoCode);
				if(network == null)
					return;
				if(listenedDerivations.ContainsKey((network.Network.NBitcoinNetwork, o.Match.DerivationStrategy)))
				{
					var chain = ChainProvider.GetChain(o.CryptoCode);
					if(chain == null)
						return;
					var blockHeader = o.BlockId == null ? null : chain.GetBlock(o.BlockId);
					await server.Send(new Models.NewTransactionEvent()
					{
						CryptoCode = o.CryptoCode,
						DerivationStrategy = o.Match.DerivationStrategy,
						BlockId = blockHeader?.HashBlock,
						TransactionData = ToTransactionResult(includeTransaction, chain, new[] { o.SavedTransaction }),
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
							r.CryptoCode = r.CryptoCode ?? cryptoCode;
							var network = Waiters.GetWaiter(r.CryptoCode)?.Network;
							if(network == null)
								break;
							foreach(var derivation in r.DerivationSchemes)
							{
								var parsed = new DerivationStrategyFactory(network.NBitcoinNetwork).Parse(derivation);
								listenedDerivations.TryAdd((network.NBitcoinNetwork, parsed), parsed);
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
			var network = GetNetwork(cryptoCode);
			var chain = this.ChainProvider.GetChain(network);
			var result = RepositoryProvider.GetRepository(network).GetSavedTransactions(txId);
			if(result.Length == 0)
				return NotFound();
			return Json(ToTransactionResult(includeTransaction, chain, result));
		}

		private TransactionResult ToTransactionResult(bool includeTransaction, ConcurrentChain chain, Repository.SavedTransaction[] result)
		{
			var noDate = NBitcoin.Utils.UnixTimeToDateTime(0);
			var oldest = result
							.Where(o => o.Timestamp != noDate)
							.OrderBy(o => o.Timestamp).FirstOrDefault() ?? result.First();

			var confBlock = result
						.Where(r => r.BlockHash != null)
						.Select(r => chain.GetBlock(r.BlockHash))
						.Where(r => r != null)
						.FirstOrDefault();

			var conf = confBlock == null ? 0 : chain.Tip.Height - confBlock.Height + 1;

			return new TransactionResult() { Confirmations = conf, Transaction = includeTransaction ? oldest.Transaction : null, Height = confBlock?.Height, Timestamp = oldest.Timestamp };
		}

		[HttpPost]
		[Route("cryptos/{cryptoCode}/derivations/{derivationStrategy}")]
		public IActionResult TrackWallet(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase derivationStrategy)
		{
			if(derivationStrategy == null)
				return NotFound();
			var network = GetNetwork(cryptoCode);
			RepositoryProvider.GetRepository(network).Track(derivationStrategy);
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
			var network = GetNetwork(cryptoCode);
			var chain = ChainProvider.GetChain(network);
			var repo = RepositoryProvider.GetRepository(network);
			var waitingTransaction = longPolling ? WaitingTransaction(extPubKey) : Task.FromResult(false);
			GetTransactionsResponse response = null;
			while(true)
			{
				response = new GetTransactionsResponse();
				int currentHeight = chain.Height;
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

					foreach(var tx in item.AnnotatedTx.Values)
					{
						BookmarkProcessor processor = new BookmarkProcessor(32 + 32 + 25);
						processor.AddData(tx.Record.Transaction.GetHash());
						processor.AddData(tx.Record.BlockHash ?? uint256.Zero);
						processor.UpdateBookmark();

						item.TxSet.Transactions.Add(new TransactionInformation()
						{
							BlockHash = tx.Record.BlockHash,
							Height = tx.Record.BlockHash == null ? null : tx.Height,
							TransactionId = tx.Record.Transaction.GetHash(),
							Transaction = includeTransaction ? tx.Record.Transaction : null,
							Confirmations = tx.Record.BlockHash == null ? 0 : currentHeight - tx.Height.Value + 1,
							Timestamp = txs.GetFirstSeen(tx.Record.Transaction.GetHash())
						});

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
			var network = GetNetwork(cryptoCode);
			var chain = ChainProvider.GetChain(network);
			var repo = RepositoryProvider.GetRepository(network);
			var waitingTransaction = longPolling ? WaitingTransaction(extPubKey) : Task.FromResult(false);
			UTXOChanges changes = null;
			var getKeyPaths = GetKeyPaths(repo, extPubKey);
			var matchScript = MatchKeyPaths(getKeyPaths);
			while(true)
			{
				changes = new UTXOChanges();
				changes.CurrentHeight = chain.Height;
				var transactions = GetAnnotatedTransactions(repo, chain, extPubKey);


				var states = UTXOStateResult.CreateStates(matchScript,
														unconfirmedBookmarks,
														transactions.UnconfirmedTransactions.Values.Select(c => c.Record.Transaction),
														confirmedBookmarks,
														transactions.ConfirmedTransactions.Values.Select(c => c.Record.Transaction));

				changes.Confirmed = SetUTXOChange(states.Confirmed);
				changes.Unconfirmed = SetUTXOChange(states.Unconfirmed, states.Confirmed.Actual);



				FillUTXOsInformation(changes.Confirmed.UTXOs, getKeyPaths, transactions, changes.CurrentHeight);
				FillUTXOsInformation(changes.Unconfirmed.UTXOs, getKeyPaths, transactions, changes.CurrentHeight);

				if(changes.HasChanges || !(await waitingTransaction))
					break;
				waitingTransaction = Task.FromResult(false); //next time, will not wait
			}
			changes.DerivationStrategy = extPubKey;
			return changes;
		}

		private void CleanConflicts(Repository repo, DerivationStrategyBase extPubKey, AnnotatedTransactionCollection transactions)
		{
			// TODO: We don't want to throw unconf transactions, as they have the timestamp of when we first saw the transaction
			// We should find a way to clean stuff out, while keeping this information

			//if(transactions.Conflicted.Length != 0)
			//{
			//	foreach(var tx in transactions.Conflicted.Select(c => c.Record))
			//	{
			//		_EventAggregator.Publish(new EvictedTransactionEvent(tx.Transaction.GetHash()));
			//	}
			//	repo.CleanTransactions(extPubKey, transactions.Conflicted.Select(c => c.Record).ToList());
			//}
		}

		static int[] MaxValue = new[] { int.MaxValue };
		private void FillUTXOsInformation(List<UTXO> utxos, Func<Script[], KeyPath[]> getKeyPaths, AnnotatedTransactionCollection transactionsById, int currentHeight)
		{
			var keyPaths = getKeyPaths(utxos.Select(u => u.ScriptPubKey).ToArray());
			for(int i = 0; i < utxos.Count; i++)
			{
				var utxo = utxos[i];
				utxo.KeyPath = keyPaths[i];
				var txHeight = transactionsById.GetByTxId(utxo.Outpoint.Hash)
									.Select(t => t.Height)
									.Where(h => h.HasValue)
									.Select(t => t.Value)
									.Concat(MaxValue)
									.Min();
				var oldest = transactionsById
					.GetByTxId(utxo.Outpoint.Hash)
					.OrderBy(o => o.Record.Inserted)
					.FirstOrDefault();
				var isUnconf = txHeight == MaxValue[0];
				utxo.Confirmations = isUnconf ? 0 : currentHeight - txHeight + 1;
				utxo.Timestamp = oldest.Record.Inserted;
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
					states.Known.UTXOByOutpoint.ContainsKey(outpoint) &&
					!substractedSpent.Contains(outpoint))
					change.SpentOutpoints.Add(outpoint);
			}
			return change;
		}

		private AnnotatedTransactionCollection GetAnnotatedTransactions(Repository repo, ConcurrentChain chain, DerivationStrategyBase extPubKey)
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

		private Func<Script[], bool[]> MatchKeyPaths(Func<Script[], KeyPath[]> getKeyPaths)
		{
			return (scripts) => getKeyPaths(scripts).Select(c => c != null).ToArray();
		}
		private Func<Script[], KeyPath[]> GetKeyPaths(Repository repo, DerivationStrategyBase extPubKey)
		{
			Dictionary<Script, KeyPath> cache = new Dictionary<Script, KeyPath>();
			return (scripts) =>
			{
				KeyPath[] result = new KeyPath[scripts.Length];
				for(int i = 0; i < result.Length; i++)
				{
					if(cache.TryGetValue(scripts[i], out KeyPath keypath))
						result[i] = keypath;
				}

				var needFetch = scripts.Where((r, i) => result[i] == null).ToArray();
				var fetched = repo.GetKeyInformations(needFetch);
				for(int i = 0; i < fetched.Length; i++)
				{
					var keyInfos = fetched[i];
					var script = needFetch[i];
					foreach(var keyInfo in keyInfos)
					{
						if(keyInfo.DerivationStrategy == extPubKey)
						{
							cache.TryAdd(script, keyInfo.KeyPath);
							break;
						}
					}
				}

				for(int i = 0; i < result.Length; i++)
				{
					if(cache.TryGetValue(scripts[i], out KeyPath keypath))
						result[i] = keypath;
				}

				return result;
			};
		}

		[HttpPost]
		[Route("cryptos/{cryptoCode}/transactions")]
		public async Task<BroadcastResult> Broadcast(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase extPubKey)
		{
			var tx = new Transaction();
			var stream = new BitcoinStream(Request.Body, false);
			tx.ReadWrite(stream);
			var network = GetNetwork(cryptoCode);
			var waiter = this.Waiters.GetWaiter(network);
			if(!waiter.RPCAvailable)
				throw RPCUnavailable();
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
