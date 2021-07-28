using NBXplorer.Logging;
using NBXplorer.ModelBinders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
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
using NBXplorer.Configuration;
using System.Net.WebSockets;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Reflection;
using NBXplorer.Analytics;

namespace NBXplorer.Controllers
{
	[Route("v1")]
	[Authorize]
	public partial class MainController : Controller
	{
		JsonSerializerSettings _SerializerSettings;
		public MainController(
			ExplorerConfiguration explorerConfiguration,
			RepositoryProvider repositoryProvider,
			ChainProvider chainProvider,
			EventAggregator eventAggregator,
			BitcoinDWaiters waiters,
			AddressPoolService addressPoolService,
			ScanUTXOSetServiceAccessor scanUTXOSetService,
			RebroadcasterHostedService rebroadcaster,
			KeyPathTemplates keyPathTemplates,
			MvcNewtonsoftJsonOptions jsonOptions,
			Analytics.FingerprintHostedService fingerprintService
			)
		{
			ExplorerConfiguration = explorerConfiguration;
			RepositoryProvider = repositoryProvider;
			ChainProvider = chainProvider;
			_SerializerSettings = jsonOptions.SerializerSettings;
			_EventAggregator = eventAggregator;
			ScanUTXOSetService = scanUTXOSetService.Instance;
			Waiters = waiters;
			Rebroadcaster = rebroadcaster;
			this.keyPathTemplates = keyPathTemplates;
			this.fingerprintService = fingerprintService;
			AddressPoolService = addressPoolService;
		}
		EventAggregator _EventAggregator;
		private readonly KeyPathTemplates keyPathTemplates;
		private readonly FingerprintHostedService fingerprintService;

		public BitcoinDWaiters Waiters
		{
			get; set;
		}
		public RebroadcasterHostedService Rebroadcaster { get; }
		public AddressPoolService AddressPoolService
		{
			get;
		}
		public ExplorerConfiguration ExplorerConfiguration { get; }
		public RepositoryProvider RepositoryProvider
		{
			get;
			private set;
		}
		public ChainProvider ChainProvider
		{
			get; set;
		}
		public ScanUTXOSetService ScanUTXOSetService { get; }

		[HttpPost]
		[Route("cryptos/{cryptoCode}/rpc")]
		[Consumes("application/json", "application/json-rpc")]
		public async Task<IActionResult> RPCProxy(string cryptoCode)
		{
			var network = GetNetwork(cryptoCode, true);
			if (!ExplorerConfiguration.ChainConfigurations.First(configuration => configuration.CryptoCode.Equals(cryptoCode, StringComparison.InvariantCultureIgnoreCase)).ExposeRPC)
			{
				throw new NBXplorerError(401, "json-rpc-not-exposed", $"JSON-RPC is not configured to be exposed.").AsException();
			}
			var waiter = Waiters.GetWaiter(network);
			var jsonRPC = string.Empty;
			using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
			{
				jsonRPC = await reader.ReadToEndAsync();
			}

			if (string.IsNullOrEmpty(jsonRPC))
			{
				throw new NBXplorerError(422, "no-json-rpc-request", $"A JSON-RPC request was not provided in the body.").AsException();
			}
			if (jsonRPC.StartsWith("["))
			{
				var batchRPC = waiter.RPC.PrepareBatch();
				var results = network.Serializer.ToObject<RPCRequest[]>(jsonRPC).Select(rpcRequest =>
				{
					rpcRequest.ThrowIfRPCError = false;
					return batchRPC.SendCommandAsync(rpcRequest);
				}).ToList();
				await batchRPC.SendBatchAsync();
				return Json(results.Select(task => task.Result));
			}

			var req = network.Serializer.ToObject<RPCRequest>(jsonRPC);
			req.ThrowIfRPCError = false;
			return Json(await waiter.RPC.SendCommandAsync(req));
		}
		
		[HttpGet]
		[Route("cryptos/{cryptoCode}/fees/{blockCount}")]
		public async Task<GetFeeRateResult> GetFeeRate(int blockCount, string cryptoCode)
		{
			var network = GetNetwork(cryptoCode, true);
			var waiter = Waiters.GetWaiter(network);
			EstimateSmartFeeResponse rate = null;
			try
			{
				rate = await waiter.RPC.TryEstimateSmartFeeAsync(blockCount);
			}
			catch (RPCException ex) when (ex.RPCCode == RPCErrorCode.RPC_METHOD_NOT_FOUND)
			{
			}
			if (rate == null)
				throw new NBXplorerError(400, "fee-estimation-unavailable", $"It is currently impossible to estimate fees, please try again later.").AsException();
			return new GetFeeRateResult() { BlockCount = rate.Blocks, FeeRate = rate.FeeRate };
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/derivations/{strategy}/addresses/unused")]
		public async Task<IActionResult> GetUnusedAddress(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase strategy, DerivationFeature feature = DerivationFeature.Deposit, int skip = 0, bool reserve = false)
		{
			if (strategy == null)
				throw new ArgumentNullException(nameof(strategy));
			var network = GetNetwork(cryptoCode, false);
			var repository = RepositoryProvider.GetRepository(network);
			if (skip >= repository.MinPoolSize)
				throw new NBXplorerError(404, "strategy-not-found", $"This strategy is not tracked, or you tried to skip too much unused addresses").AsException();
			try
			{
				var result = await repository.GetUnused(strategy, feature, skip, reserve);
				if (reserve)
				{
					while (result == null)
					{
						await AddressPoolService.GenerateAddresses(network, strategy, feature, new GenerateAddressQuery(1, null));
						result = await repository.GetUnused(strategy, feature, skip, reserve);
					}
					_ = AddressPoolService.GenerateAddresses(network, strategy, feature);
				}
				return Json(result, network.Serializer.Settings);
			}
			catch (NotSupportedException)
			{
				throw new NBXplorerError(400, "derivation-not-supported", $"The derivation scheme {feature} is not supported").AsException();
			}
		}

		[HttpPost]
		[Route("cryptos/{cryptoCode}/derivations/{strategy}/addresses/cancelreservation")]
		public async Task<IActionResult> CancelReservation(string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase strategy, [FromBody]KeyPath[] keyPaths)
		{
			var network = GetNetwork(cryptoCode, false);
			var repo = RepositoryProvider.GetRepository(network);
			await repo.CancelReservation(strategy, keyPaths);
			return Ok();
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/scripts/{script}")]
		public async Task<IActionResult> GetKeyInformations(string cryptoCode,
			[ModelBinder(BinderType = typeof(ScriptModelBinder))] Script script)
		{
			var network = GetNetwork(cryptoCode, false);
			var repo = RepositoryProvider.GetRepository(network);
			var result = (await repo.GetKeyInformations(new[] { script }))
						   .SelectMany(k => k.Value)
						   .ToArray();
			return Json(result, network.Serializer.Settings);
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/derivations/{strategy}/scripts/{script}")]
		public async Task<IActionResult> GetKeyInformations(string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase strategy,
			[ModelBinder(BinderType = typeof(ScriptModelBinder))] Script script)
		{
			var network = GetNetwork(cryptoCode, false);
			var repo = RepositoryProvider.GetRepository(network);
			var result = (await repo.GetKeyInformations(new[] { script }))
						   .SelectMany(k => k.Value)
						   .Where(k => k.DerivationStrategy == strategy)
						   .FirstOrDefault();
			if (result == null)
				throw new NBXplorerError(404, "script-not-found", "The script does not seem to be tracked").AsException();
			return Json(result, network.Serializer.Settings);
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/status")]
		public async Task<IActionResult> GetStatus(string cryptoCode)
		{
			var network = GetNetwork(cryptoCode, false);
			var waiter = Waiters.GetWaiter(network);
			var chain = ChainProvider.GetChain(network);
			var repo = RepositoryProvider.GetRepository(network);

			var location = waiter.GetLocation();
			GetBlockchainInfoResponse blockchainInfo = null;
			if (waiter.RPCAvailable)
			{
				try
				{
					var rpc = waiter.RPC.Clone();
					using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.0));
					blockchainInfo = await rpc.GetBlockchainInfoAsyncEx(cts.Token);
				}
				catch (HttpRequestException ex) when (ex.InnerException is IOException) { } // Sometimes "The response ended prematurely."
				catch (IOException) { } // Sometimes "The response ended prematurely."
				catch (OperationCanceledException) // Timeout, can happen if core is really busy
				{

				}
			}

			var status = new StatusResult()
			{
				NetworkType = network.NBitcoinNetwork.ChainName,
				CryptoCode = network.CryptoCode,
				Version = typeof(MainController).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version,
				SupportedCryptoCodes = Waiters.All().Select(w => w.Network.CryptoCode).ToArray(),
				IsFullySynched = true,
				InstanceName = ExplorerConfiguration.InstanceName
			};

			GetNetworkInfoResponse networkInfo = waiter.NetworkInfo;
			if (blockchainInfo != null && networkInfo != null)
			{
				status.BitcoinStatus = new BitcoinStatus()
				{
					IsSynched = !waiter.IsSynchingCore(blockchainInfo),
					Blocks = (int)blockchainInfo.Blocks,
					Headers = (int)blockchainInfo.Headers,
					VerificationProgress = blockchainInfo.VerificationProgress,
					MinRelayTxFee = networkInfo.GetRelayFee(),
					IncrementalRelayFee = networkInfo.GetIncrementalFee(),
					Capabilities = new NodeCapabilities()
					{
						CanScanTxoutSet = waiter.RPC.Capabilities.SupportScanUTXOSet,
						CanSupportSegwit = waiter.RPC.Capabilities.SupportSegwit,
						CanSupportTransactionCheck = waiter.RPC.Capabilities.SupportTestMempoolAccept
					},
					ExternalAddresses = (networkInfo.localaddresses ?? Array.Empty<GetNetworkInfoResponse.LocalAddress>())
										.Select(l => $"{l.address}:{l.port}").ToArray()
				};
				status.IsFullySynched &= status.BitcoinStatus.IsSynched;
			}
			status.ChainHeight = chain.Height;
			status.SyncHeight = location == null ? (int?)null : chain.FindFork(location).Height;
			status.IsFullySynched &= blockchainInfo != null
									&& waiter.State == BitcoinDWaiterState.Ready
									&& status.SyncHeight.HasValue
									&& blockchainInfo.Headers - status.SyncHeight.Value < 3;
			if (status.IsFullySynched)
			{
				var now = DateTimeOffset.UtcNow;
				await repo.Ping();
				var pingAfter = DateTimeOffset.UtcNow;
				status.RepositoryPingTime = (pingAfter - now).TotalSeconds;
				if (status.RepositoryPingTime > 30)
				{
					Logs.Explorer.LogWarning($"Repository ping exceeded 30 seconds ({(int)status.RepositoryPingTime}), please report the issue to NBXplorer developers");
				}
			}
			return Json(status, network.Serializer.Settings);
		}

		private NBXplorerNetwork GetNetwork(string cryptoCode, bool checkRPC)
		{
			if (cryptoCode == null)
				throw new ArgumentNullException(nameof(cryptoCode));
			cryptoCode = cryptoCode.ToUpperInvariant();
			var network = Waiters.GetWaiter(cryptoCode)?.Network;
			if (network == null)
				throw new NBXplorerException(new NBXplorerError(404, "cryptoCode-not-supported", $"{cryptoCode} is not supported"));

			if (checkRPC)
			{
				var waiter = Waiters.GetWaiter(network);
				if (waiter == null || !waiter.RPCAvailable || waiter.RPC.Capabilities == null)
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
			if (!HttpContext.WebSockets.IsWebSocketRequest)
				return NotFound();

			GetNetwork(cryptoCode, false); // Internally check if cryptoCode is correct

			string listenAllDerivationSchemes = null;
			string listenAllTrackedSource = null;
			var listenedBlocks = new ConcurrentDictionary<string, string>();
			var listenedDerivations = new ConcurrentDictionary<(Network, DerivationStrategyBase), DerivationStrategyBase>();
			var listenedTrackedSource = new ConcurrentDictionary<(Network, TrackedSource), TrackedSource>();

			WebsocketMessageListener server = new WebsocketMessageListener(await HttpContext.WebSockets.AcceptWebSocketAsync(), _SerializerSettings);
			CompositeDisposable subscriptions = new CompositeDisposable();
			subscriptions.Add(_EventAggregator.Subscribe<Models.NewBlockEvent>(async o =>
			{
				if (listenedBlocks.ContainsKey(o.CryptoCode))
				{
					await server.Send(o, GetSerializerSettings(o.CryptoCode));
				}
			}));
			subscriptions.Add(_EventAggregator.Subscribe<Models.NewTransactionEvent>(async o =>
			{
				var network = Waiters.GetWaiter(o.CryptoCode);
				if (network == null)
					return;

				bool forward = false;
				var derivationScheme = (o.TrackedSource as DerivationSchemeTrackedSource)?.DerivationStrategy;
				if (derivationScheme != null)
				{
					forward |= listenAllDerivationSchemes == "*" ||
								listenAllDerivationSchemes == o.CryptoCode ||
								listenedDerivations.ContainsKey((network.Network.NBitcoinNetwork, derivationScheme));
				}

				forward |= listenAllTrackedSource == "*" || listenAllTrackedSource == o.CryptoCode ||
							listenedTrackedSource.ContainsKey((network.Network.NBitcoinNetwork, o.TrackedSource));

				if (forward)
				{
					var derivation = (o.TrackedSource as DerivationSchemeTrackedSource)?.DerivationStrategy;
					await server.Send(o, GetSerializerSettings(o.CryptoCode));
				}
			}));
			try
			{
				while (server.Socket.State == WebSocketState.Open)
				{
					object message = await server.NextMessageAsync(cancellation);
					switch (message)
					{
						case Models.NewBlockEventRequest r:
							r.CryptoCode = r.CryptoCode ?? cryptoCode;
							listenedBlocks.TryAdd(r.CryptoCode, r.CryptoCode);
							break;
						case Models.NewTransactionEventRequest r:
							var network = Waiters.GetWaiter(r.CryptoCode)?.Network;
							if (r.DerivationSchemes != null)
							{
								r.CryptoCode = r.CryptoCode ?? cryptoCode;
								if (network != null)
								{
									foreach (var derivation in r.DerivationSchemes)
									{
										var parsed = network.DerivationStrategyFactory.Parse(derivation);
										listenedDerivations.TryAdd((network.NBitcoinNetwork, parsed), parsed);
									}
								}
							}
							else if (
								// Back compat: If no derivation scheme precised and ListenAllDerivationSchemes not set, we listen all
								(r.TrackedSources == null && r.ListenAllDerivationSchemes == null) ||
								(r.ListenAllDerivationSchemes != null && r.ListenAllDerivationSchemes.Value))
							{
								listenAllDerivationSchemes = r.CryptoCode;
							}

							if (r.ListenAllTrackedSource != null && r.ListenAllTrackedSource.Value)
							{
								listenAllTrackedSource = r.CryptoCode;
							}
							else if (r.TrackedSources != null)
							{
								r.CryptoCode = r.CryptoCode ?? cryptoCode;
								if (network != null)
								{
									foreach (var trackedSource in r.TrackedSources)
									{
										if (TrackedSource.TryParse(trackedSource, out var parsed, network))
											listenedTrackedSource.TryAdd((network.NBitcoinNetwork, parsed), parsed);
									}
								}
							}

							break;
						default:
							break;
					}
				}
			}
			catch when (server.Socket.State != WebSocketState.Open)
			{
			}
			finally { try { subscriptions.Dispose(); await server.DisposeAsync(cancellation); } catch { } }
			return new EmptyResult();
		}

		private JsonSerializerSettings GetSerializerSettings(string cryptoCode)
		{
			if (string.IsNullOrEmpty(cryptoCode))
				return _SerializerSettings;
			return this.GetNetwork(cryptoCode, false).JsonSerializerSettings;
		}

		[Route("cryptos/{cryptoCode}/events")]
		public async Task<JArray> GetEvents(string cryptoCode, int lastEventId = 0, int? limit = null, bool longPolling = false, CancellationToken cancellationToken = default)
		{
			if (limit != null && limit.Value < 1)
				throw new NBXplorerError(400, "invalid-limit", "limit should be more than 0").AsException();
			var network = GetNetwork(cryptoCode, false);
			TaskCompletionSource<bool> waitNextEvent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			Action<NewEventBase> maySetNextEvent = (NewEventBase ev) =>
			{
				if (ev.CryptoCode == network.CryptoCode)
					waitNextEvent.TrySetResult(true);
			};
			using (CompositeDisposable subscriptions = new CompositeDisposable())
			{
				subscriptions.Add(_EventAggregator.Subscribe<NewBlockEvent>(maySetNextEvent));
				subscriptions.Add(_EventAggregator.Subscribe<NewTransactionEvent>(maySetNextEvent));
			retry:
				var repo = RepositoryProvider.GetRepository(network);
				var result = await repo.GetEvents(lastEventId, limit);
				if (result.Count == 0 && longPolling)
				{
					try
					{
						await waitNextEvent.Task.WithCancellation(cancellationToken);
						goto retry;
					}
					catch when (cancellationToken.IsCancellationRequested)
					{

					}
				}
				return new JArray(result.Select(o => o.ToJObject(repo.Serializer.Settings)));
			}
		}


		[Route("cryptos/{cryptoCode}/events/latest")]
		public async Task<JArray> GetLatestEvents(string cryptoCode, int limit = 10)
		{
			if (limit < 1)
				throw new NBXplorerError(400, "invalid-limit", "limit should be more than 0").AsException();
			var network = GetNetwork(cryptoCode, false);
			var repo = RepositoryProvider.GetRepository(network);
			var result = await repo.GetLatestEvents(limit);
			return new JArray(result.Select(o => o.ToJObject(repo.Serializer.Settings)));
		}


		[HttpGet]
		[Route("cryptos/{cryptoCode}/transactions/{txId}")]
		public async Task<IActionResult> GetTransaction(
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 txId,
			bool includeTransaction = true,
			string cryptoCode = null)
		{
			var network = GetNetwork(cryptoCode, false);
			var chain = this.ChainProvider.GetChain(network);
			var result = await RepositoryProvider.GetRepository(network).GetSavedTransactions(txId);
			if (result.Length == 0)
			{
				var waiter = Waiters.GetWaiter(cryptoCode);
				if (waiter.RPCAvailable &&
					waiter.HasTxIndex &&
					await waiter.RPC.TryGetRawTransaction(txId) is Repository.SavedTransaction savedTransaction)
				{
					result = new[] { savedTransaction };
				}
				else
				{
					return NotFound();
				}
			}
			var tx = Utils.ToTransactionResult(chain, result);
			if (!includeTransaction)
				tx.Transaction = null;
			return Json(tx, network.Serializer.Settings);
		}

		[HttpPost]
		[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}")]
		[Route("cryptos/{cryptoCode}/addresses/{address}")]
		public async Task<IActionResult> TrackWallet(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase derivationScheme,
			[ModelBinder(BinderType = typeof(BitcoinAddressModelBinder))]
			BitcoinAddress address, [FromBody] TrackWalletRequest request = null)
		{
			request = request ?? new TrackWalletRequest();
			TrackedSource trackedSource = GetTrackedSource(derivationScheme, address);
			if (trackedSource == null)
				return NotFound();
			var network = GetNetwork(cryptoCode, false);
			if (trackedSource is DerivationSchemeTrackedSource dts)
			{
				if (request.Wait)
				{
					foreach (var feature in keyPathTemplates.GetSupportedDerivationFeatures())
					{
						await RepositoryProvider.GetRepository(network).GenerateAddresses(dts.DerivationStrategy, feature, GenerateAddressQuery(request, feature));
					}
				}
				else
				{
					foreach (var feature in keyPathTemplates.GetSupportedDerivationFeatures())
					{
						await RepositoryProvider.GetRepository(network).GenerateAddresses(dts.DerivationStrategy, feature, new GenerateAddressQuery(minAddresses: 3, null));
					}
					foreach (var feature in keyPathTemplates.GetSupportedDerivationFeatures())
					{
						_ = AddressPoolService.GenerateAddresses(network, dts.DerivationStrategy, feature, GenerateAddressQuery(request, feature));
					}
				}
			}
			else if (trackedSource is IDestination ats)
			{
				await RepositoryProvider.GetRepository(network).Track(ats);
			}
			return Ok();
		}

		private GenerateAddressQuery GenerateAddressQuery(TrackWalletRequest request, DerivationFeature feature)
		{
			if (request?.DerivationOptions == null)
				return null;
			foreach (var derivationOption in request.DerivationOptions)
			{
				if ((derivationOption.Feature is DerivationFeature f && f == feature) || derivationOption.Feature is null)
				{
					return new GenerateAddressQuery(derivationOption.MinAddresses, derivationOption.MaxAddresses);
				}
			}
			return null;
		}

		private static TrackedSource GetTrackedSource(DerivationStrategyBase derivationScheme, BitcoinAddress address)
		{
			TrackedSource trackedSource = null;
			if (address != null)
				trackedSource = new AddressTrackedSource(address);
			if (derivationScheme != null)
				trackedSource = new DerivationSchemeTrackedSource(derivationScheme);
			return trackedSource;
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}/transactions/{txId?}")]
		[Route("cryptos/{cryptoCode}/addresses/{address}/transactions/{txId?}")]
		public async Task<IActionResult> GetTransactions(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase derivationScheme,
			[ModelBinder(BinderType = typeof(BitcoinAddressModelBinder))]
			BitcoinAddress address,
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 txId = null,
			bool includeTransaction = true)
		{
			var trackedSource = GetTrackedSource(derivationScheme, address);
			if (trackedSource == null)
				throw new ArgumentNullException(nameof(trackedSource));
			TransactionInformation fetchedTransactionInfo = null;

			var network = GetNetwork(cryptoCode, false);
			var chain = ChainProvider.GetChain(network);
			var repo = RepositoryProvider.GetRepository(network);

			var response = new GetTransactionsResponse();
			int currentHeight = chain.Height;
			response.Height = currentHeight;
			var txs = await GetAnnotatedTransactions(repo, chain, trackedSource, txId);
			foreach (var item in new[]
			{
					new
					{
						TxSet = response.ConfirmedTransactions,
						AnnotatedTx = txs.ConfirmedTransactions
					},
					new
					{
						TxSet = response.UnconfirmedTransactions,
						AnnotatedTx = txs.UnconfirmedTransactions
					},
					new
					{
						TxSet = response.ReplacedTransactions,
						AnnotatedTx = txs.ReplacedTransactions
					},
				})
			{
				foreach (var tx in item.AnnotatedTx)
				{
					var txInfo = new TransactionInformation()
					{
						BlockHash = tx.Height.HasValue ? tx.Record.BlockHash : null,
						Height = tx.Height,
						TransactionId = tx.Record.TransactionHash,
						Transaction = includeTransaction ? tx.Record.Transaction : null,
						Confirmations = tx.Height.HasValue ? currentHeight - tx.Height.Value + 1 : 0,
						Timestamp = tx.Record.FirstSeen,
						Inputs = tx.Record.SpentOutpoints.Select(o => txs.GetUTXO(o)).Where(o => o != null).ToList(),
						Outputs = tx.Record.GetReceivedOutputs().ToList(),
						Replaceable = tx.Replaceable,
						ReplacedBy = tx.ReplacedBy,
						Replacing = tx.Replacing
					};

					if (txId == null || txId == txInfo.TransactionId)
						item.TxSet.Transactions.Add(txInfo);
					if (txId != null && txId == txInfo.TransactionId)
						fetchedTransactionInfo = txInfo;

					if (network.NBitcoinNetwork.NetworkSet == NBitcoin.Altcoins.Liquid.Instance)
					{
						txInfo.BalanceChange = new MoneyBag(txInfo.Outputs.Select(o => o.Value).OfType<AssetMoney>().ToArray())
												- new MoneyBag(txInfo.Inputs.Select(o => o.Value).OfType<AssetMoney>().ToArray());
					}
					else
					{
						txInfo.BalanceChange = txInfo.Outputs.Select(o => o.Value).OfType<Money>().Sum() - txInfo.Inputs.Select(o => o.Value).OfType<Money>().Sum();
					}
				}
				item.TxSet.Transactions.Reverse(); // So the youngest transaction is generally first
			}



			if (txId == null)
			{
				return Json(response, repo.Serializer.Settings);
			}
			else if (fetchedTransactionInfo == null)
			{
				return NotFound();
			}
			else
			{
				return Json(fetchedTransactionInfo, repo.Serializer.Settings);
			}
		}

		[HttpPost]
		[Route("cryptos/{cryptoCode}/rescan")]
		public async Task<IActionResult> Rescan(string cryptoCode, [FromBody] JObject body)
		{
			if (body == null)
				throw new ArgumentNullException(nameof(body));
			var rescanRequest = ParseJObject<RescanRequest>(body, GetNetwork(cryptoCode, false));
			if (rescanRequest == null)
				throw new ArgumentNullException(nameof(rescanRequest));
			if (rescanRequest?.Transactions == null)
				throw new NBXplorerException(new NBXplorerError(400, "transactions-missing", "You must specify 'transactions'"));

			bool willFetchTransactions = rescanRequest.Transactions.Any(t => t.Transaction == null);
			bool needTxIndex = rescanRequest.Transactions.Any(t => t.Transaction == null && t.BlockId == null);
			var network = GetNetwork(cryptoCode, willFetchTransactions);

			var waiter = Waiters.GetWaiter(cryptoCode);
			var rpc = waiter.RPC.PrepareBatch();
			var repo = RepositoryProvider.GetRepository(network);

			var fetchingTransactions = rescanRequest
				.Transactions
				.Select(t => FetchTransaction(rpc, waiter.HasTxIndex, t))
				.ToArray();

			await rpc.SendBatchAsync();
			await Task.WhenAll(fetchingTransactions);

			var transactions = fetchingTransactions.Select(t => t.GetAwaiter().GetResult())
												   .Where(tx => tx.Transaction != null)
												   .ToArray();

			foreach (var txs in transactions.GroupBy(t => t.BlockId, t => (t.Transaction, t.BlockTime))
											.OrderBy(t => t.First().BlockTime))
			{
				await repo.SaveTransactions(txs.First().BlockTime, txs.Select(t => t.Transaction).ToArray(), txs.Key);
				foreach (var tx in txs)
				{
					var matches = await repo.GetMatches(tx.Transaction, txs.Key, tx.BlockTime, false);
					await repo.SaveMatches(matches);
					_ = AddressPoolService.GenerateAddresses(network, matches);
				}
			}
			return Ok();
		}

		async Task<(uint256 BlockId, Transaction Transaction, DateTimeOffset BlockTime)> FetchTransaction(RPCClient rpc, bool hasTxIndex, RescanRequest.TransactionToRescan transaction)
		{
			if (transaction.Transaction != null)
			{
				if (transaction.BlockId == null)
					throw new NBXplorerException(new NBXplorerError(400, "block-id-missing", "You must specify 'transactions[].blockId' if you specified 'transactions[].transaction'"));
				var blockTime = await rpc.GetBlockTimeAsync(transaction.BlockId, false);
				if (blockTime == null)
					return (null, null, default);
				return (transaction.BlockId, transaction.Transaction, blockTime.Value);
			}
			else if (transaction.TransactionId != null)
			{
				if (transaction.BlockId != null)
				{
					try
					{
						var getTx = rpc.GetRawTransactionAsync(transaction.TransactionId, transaction.BlockId, false);
						var blockTime = await rpc.GetBlockTimeAsync(transaction.BlockId, false);
						if (blockTime == null)
							return (null, null, default);
						return (transaction.BlockId, await getTx, blockTime.Value);
					}
					catch (RPCException ex) when (ex.RPCCode == RPCErrorCode.RPC_INVALID_ADDRESS_OR_KEY)
					{
					}
				}

				if (hasTxIndex)
				{
					try
					{
						var txInfo = await rpc.GetRawTransactionInfoAsync(transaction.TransactionId);
						return (txInfo.BlockHash, txInfo.Transaction, txInfo.BlockTime.Value);
					}
					catch (RPCException ex) when (ex.RPCCode == RPCErrorCode.RPC_INVALID_ADDRESS_OR_KEY)
					{
					}
				}
				return (null, null, default);
			}
			else
			{
				throw new NBXplorerException(new NBXplorerError(400, "transaction-id-missing", "You must specify 'transactions[].transactionId' or 'transactions[].transaction'"));
			}
		}

		[HttpPost]
		[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}/metadata/{key}")]
		public async Task<IActionResult> SetMetadata(string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase derivationScheme, string key,
			[FromBody]
			JToken value = null)
		{
			var network = this.GetNetwork(cryptoCode, true);
			var trackedSource = new DerivationSchemeTrackedSource(derivationScheme);
			var repo = this.RepositoryProvider.GetRepository(network);
			await repo.SaveMetadata(trackedSource, key, value);
			return Ok();
		}
		[HttpGet]
		[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}/metadata/{key}")]
		public async Task<IActionResult> GetMetadata(string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase derivationScheme, string key)
		{
			var network = this.GetNetwork(cryptoCode, true);
			var trackedSource = new DerivationSchemeTrackedSource(derivationScheme);
			var repo = this.RepositoryProvider.GetRepository(network);
			var result = await repo.GetMetadata<JToken>(trackedSource, key);
			return result == null ? (IActionResult)NotFound() : Json(result, repo.Serializer.Settings);
		}
		Encoding UTF8 = new UTF8Encoding(false);
		[HttpPost]
		[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}/utxos/scan")]
		public IActionResult ScanUTXOSet(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase derivationScheme, int? batchSize = null, int? gapLimit = null, int? from = null)
		{
			var network = this.GetNetwork(cryptoCode, true);
			var waiter = this.Waiters.GetWaiter(network);
			if (!waiter.RPC.Capabilities.SupportScanUTXOSet)
				throw new NBXplorerError(405, "scanutxoset-not-suported", "ScanUTXOSet is not supported for this currency").AsException();

			ScanUTXOSetOptions options = new ScanUTXOSetOptions();
			if (batchSize != null)
				options.BatchSize = batchSize.Value;
			if (gapLimit != null)
				options.GapLimit = gapLimit.Value;
			if (from != null)
				options.From = from.Value;
			if (!ScanUTXOSetService.EnqueueScan(network, derivationScheme, options))
				throw new NBXplorerError(409, "scanutxoset-in-progress", "ScanUTXOSet has already been called for this derivationScheme").AsException();
			return Ok();
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}/utxos/scan")]
		public IActionResult GetScanUTXOSetInfromation(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase derivationScheme)
		{
			var network = this.GetNetwork(cryptoCode, false);
			var info = ScanUTXOSetService.GetInformation(network, derivationScheme);
			if (info == null)
				throw new NBXplorerError(404, "scanutxoset-info-not-found", "ScanUTXOSet has not been called with this derivationScheme of the result has expired").AsException();
			return Json(info, network.Serializer.Settings);
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}/balance")]
		[Route("cryptos/{cryptoCode}/addresses/{address}/balance")]
		public async Task<IActionResult> GetBalance(string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase derivationScheme,
			[ModelBinder(BinderType = typeof(BitcoinAddressModelBinder))]
			BitcoinAddress address)
		{
			var getTransactionsResult = await GetTransactions(cryptoCode, derivationScheme, address);
			var jsonResult = getTransactionsResult as JsonResult;
			var transactions = jsonResult?.Value as GetTransactionsResponse;
			if (transactions == null)
				return getTransactionsResult;

			var network = this.GetNetwork(cryptoCode, false);
			var balance = new GetBalanceResponse()
			{
				Confirmed = CalculateBalance(network, transactions.ConfirmedTransactions),
				Unconfirmed = CalculateBalance(network, transactions.UnconfirmedTransactions)
			};
			balance.Total = balance.Confirmed.Add(balance.Unconfirmed);
			return Json(balance, jsonResult.SerializerSettings);
		}

		private IMoney CalculateBalance(NBXplorerNetwork network, TransactionInformationSet transactions)
		{
			if (network.NBitcoinNetwork.NetworkSet == NBitcoin.Altcoins.Liquid.Instance)
			{
				return new MoneyBag(transactions.Transactions.Select(t => t.BalanceChange).ToArray());
			}
			else
			{
				return transactions.Transactions.Select(t => t.BalanceChange).OfType<Money>().Sum();
			}
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}/utxos")]
		[Route("cryptos/{cryptoCode}/addresses/{address}/utxos")]
		public async Task<IActionResult> GetUTXOs(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase derivationScheme,
			[ModelBinder(BinderType = typeof(BitcoinAddressModelBinder))]
			BitcoinAddress address)
		{
			var trackedSource = GetTrackedSource(derivationScheme, address);
			UTXOChanges changes = null;
			if (trackedSource == null)
				throw new ArgumentNullException(nameof(trackedSource));


			var network = GetNetwork(cryptoCode, false);
			var chain = ChainProvider.GetChain(network);
			var repo = RepositoryProvider.GetRepository(network);

			changes = new UTXOChanges();
			changes.CurrentHeight = chain.Height;
			var transactions = await GetAnnotatedTransactions(repo, chain, trackedSource);

			changes.Confirmed = ToUTXOChange(transactions.ConfirmedState);
			changes.Confirmed.SpentOutpoints.Clear();
			changes.Unconfirmed = ToUTXOChange(transactions.UnconfirmedState - transactions.ConfirmedState);



			FillUTXOsInformation(changes.Confirmed.UTXOs, transactions, changes.CurrentHeight);
			FillUTXOsInformation(changes.Unconfirmed.UTXOs, transactions, changes.CurrentHeight);

			changes.TrackedSource = trackedSource;
			changes.DerivationStrategy = (trackedSource as DerivationSchemeTrackedSource)?.DerivationStrategy;

			return Json(changes, repo.Serializer.Settings);
		}

		private UTXOChange ToUTXOChange(UTXOState state)
		{
			UTXOChange change = new UTXOChange();
			change.SpentOutpoints.AddRange(state.SpentUTXOs);
			change.UTXOs.AddRange(state.UTXOByOutpoint.Select(u => new UTXO(u.Value)));
			return change;
		}

		private static bool IsMatching(TrackedSource trackedSource, Script s, AnnotatedTransactionCollection transactions)
		{
			if (trackedSource is DerivationSchemeTrackedSource dsts)
				return transactions.GetKeyPath(s) != null;
			else if (trackedSource is IDestination addr)
				return addr.ScriptPubKey == s;
			else
				throw new NotSupportedException();
		}

		int MaxHeight = int.MaxValue;
		private void FillUTXOsInformation(List<UTXO> utxos, AnnotatedTransactionCollection transactions, int currentHeight)
		{
			for (int i = 0; i < utxos.Count; i++)
			{
				var utxo = utxos[i];
				utxo.KeyPath = transactions.GetKeyPath(utxo.ScriptPubKey);
				if (utxo.KeyPath != null)
					utxo.Feature = keyPathTemplates.GetDerivationFeature(utxo.KeyPath);
				var txHeight = transactions.GetByTxId(utxo.Outpoint.Hash).Height is int h ? h : MaxHeight;
				var isUnconf = txHeight == MaxHeight;
				utxo.Confirmations = isUnconf ? 0 : currentHeight - txHeight + 1;
				utxo.Timestamp = transactions.GetByTxId(utxo.Outpoint.Hash).Record.FirstSeen;
			}
		}

		private async Task<AnnotatedTransactionCollection> GetAnnotatedTransactions(Repository repo, SlimChain chain, TrackedSource trackedSource, uint256 txId = null)
		{
			var transactions = await repo.GetTransactions(trackedSource, txId, this.HttpContext.RequestAborted);

			// If the called is interested by only a single txId, we need to fetch the parents as well
			if (txId != null)
			{
				var spentOutpoints = transactions.SelectMany(t => t.SpentOutpoints.Select(o => o.Hash)).ToHashSet();
				var gettingParents = spentOutpoints.Select(async h => await repo.GetTransactions(trackedSource, h)).ToList();
				await Task.WhenAll(gettingParents);
				transactions = gettingParents.SelectMany(p => p.GetAwaiter().GetResult()).Concat(transactions).ToArray();
			}

			var annotatedTransactions = new AnnotatedTransactionCollection(transactions, trackedSource, chain, repo.Network.NBitcoinNetwork);

			Rebroadcaster.RebroadcastPeriodically(repo.Network, trackedSource, annotatedTransactions.UnconfirmedTransactions
																				.Concat(annotatedTransactions.CleanupTransactions).Select(c => c.Record.Key).ToArray());
			return annotatedTransactions;
		}

		[HttpPost]
		[Route("cryptos/{cryptoCode}/transactions")]
		public async Task<BroadcastResult> Broadcast(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase extPubKey, // For back compat
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase derivationScheme,
			[ModelBinder(BinderType = typeof(BitcoinAddressModelBinder))]
			BitcoinAddress address, bool testMempoolAccept = false)
		{
			var network = GetNetwork(cryptoCode, true);
			var trackedSource = GetTrackedSource(derivationScheme ?? extPubKey, address);
			var tx = network.NBitcoinNetwork.Consensus.ConsensusFactory.CreateTransaction();
			var buffer = new MemoryStream();
			await Request.Body.CopyToAsync(buffer);
			buffer.Position = 0;
			tx.FromBytes(buffer.ToArrayEfficient());

			var waiter = this.Waiters.GetWaiter(network);
			if (testMempoolAccept && !waiter.RPC.Capabilities.SupportTestMempoolAccept)
				throw new NBXplorerException(new NBXplorerError(400, "not-supported", "This feature is not supported for this crypto currency"));
			var repo = RepositoryProvider.GetRepository(network);
			var chain = ChainProvider.GetChain(network);
			RPCException rpcEx = null;
			try
			{
				if (testMempoolAccept)
				{
					var mempoolAccept = await waiter.RPC.TestMempoolAcceptAsync(tx, default);
					if (mempoolAccept.IsAllowed)
						return new BroadcastResult(true);
					var rpcCode = GetRPCCodeFromReason(mempoolAccept.RejectReason);
					return new BroadcastResult(false)
					{
						RPCCode = rpcCode,
						RPCMessage = rpcCode == RPCErrorCode.RPC_TRANSACTION_REJECTED ? "Transaction was rejected by network rules" : null,
						RPCCodeMessage = mempoolAccept.RejectReason,
					};
				}
				await waiter.RPC.SendRawTransactionAsync(tx);
				await waiter.GetExplorerBehavior()?.SaveMatches(tx, false);
				return new BroadcastResult(true);
			}
			catch (RPCException ex) when (!testMempoolAccept)
			{
				rpcEx = ex;
				Logs.Explorer.LogInformation($"{network.CryptoCode}: Transaction {tx.GetHash()} failed to broadcast (Code: {ex.RPCCode}, Message: {ex.RPCCodeMessage}, Details: {ex.Message} )");
				if (trackedSource != null && ex.Message.StartsWith("Missing inputs", StringComparison.OrdinalIgnoreCase))
				{
					Logs.Explorer.LogInformation($"{network.CryptoCode}: Trying to broadcast unconfirmed of the wallet");
					var transactions = await GetAnnotatedTransactions(repo, chain, trackedSource);
					foreach (var existing in transactions.UnconfirmedTransactions)
					{
						var t = existing.Record.Transaction ?? (await repo.GetSavedTransactions(existing.Record.TransactionHash)).Select(c => c.Transaction).FirstOrDefault();
						if (t == null)
							continue;
						try
						{
							await waiter.RPC.SendRawTransactionAsync(t);
						}
						catch { }
					}

					try
					{
						await waiter.RPC.SendRawTransactionAsync(tx);
						Logs.Explorer.LogInformation($"{network.CryptoCode}: Broadcast success");
						await waiter.GetExplorerBehavior()?.SaveMatches(tx, false);
						return new BroadcastResult(true);
					}
					catch (RPCException)
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

		private RPCErrorCode? GetRPCCodeFromReason(string rejectReason)
		{
			return rejectReason switch
			{
				"Transaction already in block chain" => RPCErrorCode.RPC_VERIFY_ALREADY_IN_CHAIN,
				"Transaction rejected by AcceptToMemoryPool" => RPCErrorCode. RPC_TRANSACTION_REJECTED,
				"AcceptToMemoryPool failed" => RPCErrorCode. RPC_TRANSACTION_REJECTED,
				"insufficient fee" => RPCErrorCode. RPC_TRANSACTION_REJECTED,
				_ => RPCErrorCode. RPC_TRANSACTION_ERROR
			};
		}

		[HttpPost]
		[Route("cryptos/{cryptoCode}/derivations")]
		public async Task<IActionResult> GenerateWallet(string cryptoCode, [FromBody] GenerateWalletRequest request)
		{
			if (request == null)
				request = new GenerateWalletRequest();
			var network = GetNetwork(cryptoCode, request.ImportKeysToRPC);
			if (network.CoinType == null)
				// Don't document, only shitcoins nobody use goes into this
				throw new NBXplorerException(new NBXplorerError(400, "not-supported", "This feature is not supported for this coin because we don't have CoinType information"));
			request.WordList ??= Wordlist.English;
			request.WordCount ??= WordCount.Twelve;
			request.ScriptPubKeyType ??= ScriptPubKeyType.Segwit;
			if (request.ScriptPubKeyType is null)
			{
				request.ScriptPubKeyType = network.NBitcoinNetwork.Consensus.SupportSegwit ? ScriptPubKeyType.Segwit : ScriptPubKeyType.Legacy;
			}
			if (!network.NBitcoinNetwork.Consensus.SupportSegwit && request.ScriptPubKeyType != ScriptPubKeyType.Legacy)
				throw new NBXplorerException(new NBXplorerError(400, "segwit-not-supported", "Segwit is not supported, please explicitely set scriptPubKeyType to Legacy"));

			var repo = RepositoryProvider.GetRepository(network);
			Mnemonic mnemonic = null;
			if (request.ExistingMnemonic != null)
			{
				try
				{
					mnemonic = new Mnemonic(request.ExistingMnemonic, request.WordList);
				}
				catch
				{
					throw new NBXplorerException(new NBXplorerError(400, "invalid-mnemonic", "Invalid mnemonic words"));
				}
			}
			else
			{
				mnemonic = new Mnemonic(request.WordList, request.WordCount.Value);
			}
			var masterKey = mnemonic.DeriveExtKey(request.Passphrase).GetWif(network.NBitcoinNetwork);
			var keyPath = GetDerivationKeyPath(request.ScriptPubKeyType.Value, request.AccountNumber, network);
			var accountKey = masterKey.Derive(keyPath);
			DerivationStrategyBase derivation = network.DerivationStrategyFactory.CreateDirectDerivationStrategy(accountKey.Neuter(), new DerivationStrategyOptions()
			{
				ScriptPubKeyType = request.ScriptPubKeyType.Value
			});

			var derivationTrackedSource = new DerivationSchemeTrackedSource(derivation);
			List<Task> saveMetadata = new List<Task>();
			if (request.SavePrivateKeys)
			{
				saveMetadata.AddRange(
				new[] {
					repo.SaveMetadata(derivationTrackedSource, WellknownMetadataKeys.Mnemonic, mnemonic.ToString()),
					repo.SaveMetadata(derivationTrackedSource, WellknownMetadataKeys.MasterHDKey, masterKey),
					repo.SaveMetadata(derivationTrackedSource, WellknownMetadataKeys.AccountHDKey, accountKey)
				});
			}

			var accountKeyPath = new RootedKeyPath(masterKey.GetPublicKey().GetHDFingerPrint(), keyPath);
			saveMetadata.Add(repo.SaveMetadata(derivationTrackedSource, WellknownMetadataKeys.AccountKeyPath, accountKeyPath));
			saveMetadata.Add(repo.SaveMetadata<string>(derivationTrackedSource, WellknownMetadataKeys.ImportAddressToRPC, request.ImportKeysToRPC.ToString()));
			await Task.WhenAll(saveMetadata.ToArray());
			await TrackWallet(cryptoCode, derivation, null);
			return Json(new GenerateWalletResponse()
			{
				MasterHDKey = masterKey,
				AccountHDKey = accountKey,
				AccountKeyPath = accountKeyPath,
				DerivationScheme = derivation,
				Mnemonic = mnemonic.ToString(),
				Passphrase = request.Passphrase ?? string.Empty,
				WordCount = request.WordCount.Value,
				WordList = request.WordList
			}, network.Serializer.Settings);
		}

		private KeyPath GetDerivationKeyPath(ScriptPubKeyType scriptPubKeyType, int accountNumber, NBXplorerNetwork network)
		{
			var keyPath = new KeyPath(scriptPubKeyType == ScriptPubKeyType.Legacy ? "44'" :
				scriptPubKeyType == ScriptPubKeyType.Segwit ? "84'" :
				scriptPubKeyType == ScriptPubKeyType.SegwitP2SH ? "49'" :
				throw new NotSupportedException(scriptPubKeyType.ToString())); // Should never happen
			return keyPath.Derive(network.CoinType)
				   .Derive(accountNumber, true);
		}

		[HttpPost]
		[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}/prune")]
		public async Task<PruneResponse> Prune(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase derivationScheme, [FromBody] PruneRequest request)
		{
			request ??= new PruneRequest();
			request.DaysToKeep ??= 1.0;
			var trackedSource = new DerivationSchemeTrackedSource(derivationScheme);
			var network = GetNetwork(cryptoCode, false);
			var chain = ChainProvider.GetChain(network);
			var repo = RepositoryProvider.GetRepository(network);
			var transactions = await GetAnnotatedTransactions(repo, chain, trackedSource);
			var state = transactions.ConfirmedState;
			var prunableIds = new HashSet<uint256>();

			var keepConfMax = network.NBitcoinNetwork.Consensus.GetExpectedBlocksFor(TimeSpan.FromDays(request.DaysToKeep.Value));
			var tip = chain.Height;
			// Step 1. We can prune if all UTXOs are spent
			foreach (var tx in transactions.ConfirmedTransactions)
			{
				if (tx.Height is int h && tip - h + 1 > keepConfMax)
				{
					if (tx.Record.ReceivedCoins.All(c => state.SpentUTXOs.Contains(c.Outpoint)))
					{
						prunableIds.Add(tx.Record.Key.TxId);
					}
				}
			}

			// Step2. However, we need to remove those who are spending a UTXO from a transaction that is not pruned
			retry:
			bool removedPrunables = false;
			if (prunableIds.Count != 0)
			{
				foreach (var tx in transactions.ConfirmedTransactions)
				{
					if (prunableIds.Count == 0)
						break;
					if (!prunableIds.Contains(tx.Record.TransactionHash))
						continue;
					foreach (var parent in tx.Record.SpentOutpoints
													.Select(spent => transactions.GetByTxId(spent.Hash))
													.Where(parent => parent != null)
													.Where(parent => !prunableIds.Contains(parent.Record.TransactionHash)))
					{
						prunableIds.Remove(tx.Record.TransactionHash);
						removedPrunables = true;
					}
				}
			}
			// If we removed some prunable, it may have made other transactions unprunable.
			if (removedPrunables)
				goto retry;

			if (prunableIds.Count != 0)
			{
				await repo.Prune(trackedSource, prunableIds
												.Select(id => transactions.GetByTxId(id).Record)
												.ToList());
				Logs.Explorer.LogInformation($"{network.CryptoCode}: Pruned {prunableIds.Count} transactions");
			}
			return new PruneResponse() { TotalPruned = prunableIds.Count };
		}
	}
}
