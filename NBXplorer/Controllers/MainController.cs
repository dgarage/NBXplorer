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
			ExplorerConfiguration config,
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
			ExplorerConfiguration = config;
		}

		EventAggregator _EventAggregator;

		public BitcoinDWaiters Waiters
		{
			get; set;
		}
		public ExplorerConfiguration ExplorerConfiguration
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
			var network = GetNetwork(cryptoCode);
			if(!network.SupportEstimatesSmartFee)
			{
				throw new NBXplorerError(400, "fee-estimation-unavailable", $"{cryptoCode} does not support estimatesmartfee").AsException();
			}
			var waiter = GetWaiter(network);
			var result = await waiter.RPC.SendCommandAsync("estimatesmartfee", blockCount);
			var obj = (JObject)result.Result;
			var feeRateProperty = obj.Property("feerate");
			var rate = feeRateProperty == null ? (decimal)-1 : obj["feerate"].Value<decimal>();
			FeeRate feeRate = rate == -1 ? GetDefaultFeeRate(cryptoCode) : new FeeRate(Money.Coins(Math.Round(rate / 1000, 8)), 1);
			if(feeRate == null)
			{
				throw new NBXplorerError(400, "fee-estimation-unavailable", $"It is currently impossible to estimate fees, please try again later.").AsException();
			}

			return new GetFeeRateResult()
			{
				FeeRate = feeRate,
				BlockCount = obj["blocks"].Value<int>()
			};
		}

		private FeeRate GetDefaultFeeRate(string cryptoCode)
		{
			return ExplorerConfiguration.ChainConfigurations.FirstOrDefault(c => c.CryptoCode.Equals(cryptoCode, StringComparison.OrdinalIgnoreCase))?.FallbackFeeRate;
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
		[Route("cryptos/{cryptoCode}/derivations/{strategy}/addresses")]
		public KeyPathInformation GetKeyInformationFromKeyPath(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase strategy,
			[ModelBinder(BinderType = typeof(KeyPathModelBinder))]
			KeyPath keyPath)
		{
			if(strategy == null)
				throw new ArgumentNullException(nameof(strategy));
			if(keyPath == null)
				throw new ArgumentNullException(nameof(keyPath));
			var network = GetNetwork(cryptoCode);
			var information = strategy.Derive(keyPath);
			return new KeyPathInformation()
			{
				Address = information.ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork).ToString(),
				DerivationStrategy = strategy,
				KeyPath = keyPath,
				ScriptPubKey = information.ScriptPubKey,
				Redeem = information.Redeem,
				Feature = DerivationStrategyBase.GetFeature(keyPath)
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
		public async Task<IActionResult> CancelReservation(string cryptoCode, [ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase strategy, [FromBody]KeyPath[] keyPaths)
		{
			var network = GetNetwork(cryptoCode);
			var repo = RepositoryProvider.GetRepository(network);
			await repo.CancelReservation(strategy, keyPaths);
			return Ok();
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/scripts/{script}")]
		public async Task<IActionResult> GetKeyInformations(string cryptoCode,
			[ModelBinder(BinderType = typeof(ScriptModelBinder))] Script script)
		{
			var network = GetNetwork(cryptoCode);
			var repo = RepositoryProvider.GetRepository(network);
			var result = (await repo.GetKeyInformations(new[] { script }))
						   .SelectMany(k => k.Value)
						   .ToArray();
			return Json(result);
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
			CancellationToken cancellation = default)
		{
			if(!HttpContext.WebSockets.IsWebSocketRequest)
				return NotFound();

			GetNetwork(cryptoCode); // Internally check if cryptoCode is correct

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
		public async Task<IActionResult> GetTransaction(
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 txId,
			bool includeTransaction = true,
			string cryptoCode = null)
		{
			var network = GetNetwork(cryptoCode);
			var chain = this.ChainProvider.GetChain(network);
			var result = await RepositoryProvider.GetRepository(network).GetSavedTransactions(txId);
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

			return new TransactionResult() { Confirmations = conf, BlockId = confBlock?.HashBlock, Transaction = includeTransaction ? oldest.Transaction : null, Height = confBlock?.Height, Timestamp = oldest.Timestamp };
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
			var network = GetNetwork(cryptoCode);
			await RepositoryProvider.GetRepository(network).Track(derivationStrategy);
			return Ok();
		}

		[HttpPost]
		[Route("cryptos/{cryptoCode}/locks/{unlockId}/cancel")]
		public async Task<IActionResult> UnlockUTXOs(string cryptoCode, string unlockId)
		{
			var network = GetNetwork(cryptoCode);
			var repo = RepositoryProvider.GetRepository(network);
			if(await repo.CancelMatches(unlockId))
				return Ok();
			else
				return NotFound("unlockId not found");
		}


		[HttpPost]
		[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}/transactions")]
		public async Task<LockUTXOsResponse> LockUTXOs(string cryptoCode,
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase derivationScheme,
			[FromBody] LockUTXOsRequest request,
			CancellationToken cancellation = default)
		{
			if(derivationScheme == null)
				throw new ArgumentNullException(nameof(derivationScheme));
			if(request?.Destination == null)
				throw new NBXplorerException(new NBXplorerError(400, "invalid-destination", "Invalid destination address"));

			var network = GetNetwork(cryptoCode);

			BitcoinAddress destinationAddress = null;
			try
			{
				destinationAddress = BitcoinAddress.Create(request.Destination, network.NBitcoinNetwork);
			}
			catch
			{
				throw new NBXplorerException(new NBXplorerError(400, "invalid-destination", "Invalid destination address"));
			}
			if(request.Amount == null || request.Amount <= Money.Zero)
				throw new NBXplorerException(new NBXplorerError(400, "invalid-amount", "amount should be equal or less than 0 satoshi"));

			var firstAddress = derivationScheme.Derive(new KeyPath("0")).ScriptPubKey;
			if(!firstAddress.IsPayToScriptHash && !firstAddress.IsWitness)
				throw new NBXplorerException(new NBXplorerError(400, "invalid-derivationScheme", "Only P2SH or segwit derivation schemes are supported"));

			var chain = ChainProvider.GetChain(network);
			var repo = RepositoryProvider.GetRepository(network);

			var feeRate = request.FeeRate ?? (await this.GetFeeRate(6, cryptoCode)).FeeRate;
			var change = await repo.GetUnused(derivationScheme, DerivationFeature.Change, 0, true);

			Repository.DBLock walletLock = null;
			try
			{
				walletLock = await repo.TakeWalletLock(derivationScheme, cancellation);
				var changes = new UTXOChanges();
				var transactions = await GetAnnotatedTransactions(repo, chain, derivationScheme, true);
				Func<Transaction, Script[], bool[]> matchScript = (t, scripts) => scripts.Select(s => t.IsLockUTXO() ? false : transactions.GetKeyPath(s) != null).ToArray();

				var states = UTXOStateResult.CreateStates(matchScript,
															new HashSet<Bookmark>(),
															transactions.UnconfirmedTransactions.Values.Select(c => c.Record.Transaction),
															new HashSet<Bookmark>(),
															transactions.ConfirmedTransactions.Values.Select(c => c.Record.Transaction));

				changes.Confirmed = SetUTXOChange(states.Confirmed);
				changes.Unconfirmed = SetUTXOChange(states.Unconfirmed, states.Confirmed.Actual);

				// TODO: We might want to cache this as the Derive operation is computionally expensive
				var unspentCoins = changes.GetUnspentCoins()
					   .Select(c => Task.Run(() => c.ToScriptCoin(derivationScheme.Derive(transactions.GetKeyPath(c.ScriptPubKey)).Redeem)))
						 .Select(t => t.Result)
						 .ToArray();

				var txBuilder = new TransactionBuilder();
				txBuilder.SetConsensusFactory(network.NBitcoinNetwork);
				txBuilder.AddCoins(unspentCoins);
				txBuilder.Send(destinationAddress, request.Amount);
				txBuilder.SetChange(change.ScriptPubKey);
				txBuilder.SendEstimatedFees(feeRate);
				txBuilder.Shuffle();
				var tx = txBuilder.BuildTransaction(false);

				LockUTXOsResponse result = new LockUTXOsResponse();
				var spentCoins = txBuilder.FindSpentCoins(tx).OfType<ScriptCoin>().ToArray();
				result.SpentCoins = spentCoins.Select(r => new LockUTXOsResponse.SpentCoin()
				{
					KeyPath = transactions.GetKeyPath(r.ScriptPubKey),
					Outpoint = r.Outpoint,
					Value = r.Amount
				})
					.ToArray();
				foreach(var input in tx.Inputs)
				{
					var coin = spentCoins.Single(s => s.Outpoint == input.PrevOut);
					if(coin.RedeemType == RedeemType.P2SH)
					{
						input.ScriptSig = new Script(Op.GetPushOp(coin.Redeem.ToBytes()));
					}
					else if(coin.RedeemType == RedeemType.WitnessV0)
					{
						input.WitScript = new Script(Op.GetPushOp(coin.Redeem.ToBytes()));
						if(coin.IsP2SH)
							input.ScriptSig = new Script(Op.GetPushOp(coin.Redeem.WitHash.ScriptPubKey.ToBytes()));
					}
				}
				result.Transaction = tx.Clone();

				var changeOutput = tx.Outputs.Where(c => c.ScriptPubKey == change.ScriptPubKey).FirstOrDefault();
				if(result.Transaction.Outputs.Count > 1 && changeOutput != null)
				{
					result.ChangeInformation = new LockUTXOsResponse.ChangeInfo()
					{
						KeyPath = change.KeyPath,
						Value = changeOutput.Value
					};
				}
				tx.MarkLockUTXO();
				var matches = await repo.GetMatches(tx);
				var cancellableMatch = await repo.SaveMatches(DateTimeOffset.UtcNow,
					matches
					.Select(m => new MatchedTransaction()
					{
						BlockId = null,
						Match = m
					})
					.ToArray(), true);
				result.UnlockId = cancellableMatch.Key;
				return result;
			}
			catch(NotEnoughFundsException)
			{
				throw new NBXplorerException(new NBXplorerError(400, "not-enough-funds", "Not enough funds for doing this transaction"));
			}
			finally
			{
				if(walletLock != null)
					await walletLock.ReleaseLock();
			}
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
				response.Height = currentHeight;
				var txs = await GetAnnotatedTransactions(repo, chain, extPubKey, false);
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

		//[HttpPost]
		//[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}/")]
		//public async Task<>


		[HttpGet]
		[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}/balances")]
		public async Task<GetBalanceResponse> GetBalance(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DestinationModelBinder))]
			DerivationStrategyBase derivationScheme)
		{
			var network = GetNetwork(cryptoCode);
			var chain = ChainProvider.GetChain(network);
			var repo = RepositoryProvider.GetRepository(network);

			GetBalanceResponse response = new GetBalanceResponse();

			var transactions = await GetAnnotatedTransactions(repo, chain, derivationScheme, true);

			response.Spendable = CalculateBalance(transactions, true);
			response.Total = CalculateBalance(transactions, false);

			return response;
		}

		private Money CalculateBalance(AnnotatedTransactionCollection transactions, bool spendableOnly)
		{
			var changes = new UTXOChanges();
			Func<Transaction, Script[], bool[]> matchScript = (tx, scripts) => scripts.Select(s => spendableOnly && tx.IsLockUTXO() ? false : transactions.GetKeyPath(s) != null).ToArray();

			var states = UTXOStateResult.CreateStates(matchScript,
														new HashSet<Bookmark>(),
														transactions.UnconfirmedTransactions.Values.Select(c => c.Record.Transaction),
														new HashSet<Bookmark>(),
														transactions.ConfirmedTransactions.Values.Select(c => c.Record.Transaction));

			changes.Confirmed = SetUTXOChange(states.Confirmed);
			changes.Unconfirmed = SetUTXOChange(states.Unconfirmed, states.Confirmed.Actual);

			return changes.GetUnspentCoins().Select(c => c.Amount).Sum();
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

			while(true)
			{
				changes = new UTXOChanges();
				changes.CurrentHeight = chain.Height;
				var transactions = await GetAnnotatedTransactions(repo, chain, extPubKey, true);
				Func<Transaction, Script[], bool[]> matchScript = (tx, scripts) => scripts.Select(s => tx.IsLockUTXO() ? false : transactions.GetKeyPath(s) != null).ToArray();

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

		private async Task CleanConflicts(Repository repo, DerivationStrategyBase extPubKey, AnnotatedTransactionCollection transactions)
		{
			var cleaned = transactions.DuplicatedTransactions.Where(c => (DateTimeOffset.UtcNow - c.Record.Inserted) > TimeSpan.FromDays(1.0)).Select(c => c.Record).ToArray();
			if(cleaned.Length != 0)
			{
				foreach(var tx in cleaned)
				{
					_EventAggregator.Publish(new EvictedTransactionEvent(tx.Transaction.GetHash()));
				}
				await repo.CleanTransactions(extPubKey, cleaned.ToList());
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

		private async Task<AnnotatedTransactionCollection> GetAnnotatedTransactions(Repository repo, ConcurrentChain chain, DerivationStrategyBase extPubKey, bool includeLocks)
		{
			var annotatedTransactions = new AnnotatedTransactionCollection((await repo
				.GetTransactions(extPubKey))
				.Where(t => includeLocks || !t.Transaction.IsLockUTXO())
				.Select(t => new AnnotatedTransaction(t, chain))
				.ToList());
			await CleanConflicts(repo, extPubKey, annotatedTransactions);
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
			var network = GetNetwork(cryptoCode);

			var tx = network.NBitcoinNetwork.Consensus.ConsensusFactory.CreateTransaction();
			var stream = new BitcoinStream(Request.Body, false);
			tx.ReadWrite(stream);

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
					var transactions = await GetAnnotatedTransactions(repo, chain, extPubKey, false);
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
