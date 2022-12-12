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
using NBXplorer.Backends;
using NBitcoin.Scripting;
using System.Globalization;

namespace NBXplorer.Controllers
{
	[Route("v1")]
	[Authorize]
	public partial class MainController : ControllerBase, IUTXOService
	{
		JsonSerializerSettings _SerializerSettings;
		public MainController(
			ExplorerConfiguration explorerConfiguration,
			IRepositoryProvider repositoryProvider,
			EventAggregator eventAggregator,
			IRPCClients rpcClients,
			AddressPoolService addressPoolService,
			ScanUTXOSetServiceAccessor scanUTXOSetService,
			RebroadcasterHostedService rebroadcaster,
			KeyPathTemplates keyPathTemplates,
			MvcNewtonsoftJsonOptions jsonOptions,
			NBXplorerNetworkProvider networkProvider,
			Analytics.FingerprintHostedService fingerprintService,
			IIndexers indexers
			): base(networkProvider, rpcClients, repositoryProvider, indexers)
		{
			ExplorerConfiguration = explorerConfiguration;
			_SerializerSettings = jsonOptions.SerializerSettings;
			_EventAggregator = eventAggregator;
			ScanUTXOSetService = scanUTXOSetService.Instance;
			Rebroadcaster = rebroadcaster;
			this.keyPathTemplates = keyPathTemplates;
			this.fingerprintService = fingerprintService;
			AddressPoolService = addressPoolService;
		}
		EventAggregator _EventAggregator;
		private readonly KeyPathTemplates keyPathTemplates;
		private readonly FingerprintHostedService fingerprintService;

		public RebroadcasterHostedService Rebroadcaster { get; }
		public AddressPoolService AddressPoolService
		{
			get;
		}
		public ExplorerConfiguration ExplorerConfiguration { get; }
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
			var rpc = RPCClients.Get(network);
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
				var batchRPC = rpc.PrepareBatch();
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
			return Json(await rpc.SendCommandAsync(req));
		}

		[HttpGet]
		[Route("cryptos/{cryptoCode}/fees/{blockCount}")]
		public async Task<GetFeeRateResult> GetFeeRate(int blockCount, string cryptoCode)
		{
			var network = GetNetwork(cryptoCode, true);
			var rpc = RPCClients.Get(network);
			EstimateSmartFeeResponse rate = null;
			try
			{
				rate = await rpc.TryEstimateSmartFeeAsync(blockCount);
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
			DerivationStrategyBase strategy, [FromBody] KeyPath[] keyPaths)
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
			var indexer = Indexers.GetIndexer(network);
			var rpc = indexer.GetConnectedClient();
			GetBlockchainInfoResponse blockchainInfo = null;
			if (rpc is not null)
			{
				try
				{
					var rpc2 = rpc.Clone();
					using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10.0));
					blockchainInfo = await rpc2.GetBlockchainInfoAsyncEx(cts.Token);
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
				Backend = ExplorerConfiguration.IsPostgres ? "Postgres" : "DBTrie",
				CryptoCode = network.CryptoCode,
				Version = typeof(MainController).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version,
				SupportedCryptoCodes = Indexers.All().Select(w => w.Network.CryptoCode).ToArray(),
				IsFullySynched = true,
				InstanceName = ExplorerConfiguration.InstanceName
			};

			GetNetworkInfoResponse networkInfo = indexer.NetworkInfo;
			if (blockchainInfo != null && networkInfo != null)
			{
				status.BitcoinStatus = new BitcoinStatus()
				{
					IsSynched = !blockchainInfo.IsSynching(network),
					Blocks = (int)blockchainInfo.Blocks,
					Headers = (int)blockchainInfo.Headers,
					VerificationProgress = blockchainInfo.VerificationProgress,
					MinRelayTxFee = networkInfo.GetRelayFee(),
					IncrementalRelayFee = networkInfo.GetIncrementalFee(),
					Capabilities = new NodeCapabilities()
					{
						CanScanTxoutSet = rpc.Capabilities.SupportScanUTXOSet,
						CanSupportSegwit = rpc.Capabilities.SupportSegwit,
						CanSupportTaproot = rpc.Capabilities.SupportTaproot,
						CanSupportTransactionCheck = rpc.Capabilities.SupportTestMempoolAccept
					},
					ExternalAddresses = (networkInfo.localaddresses ?? Array.Empty<GetNetworkInfoResponse.LocalAddress>())
										.Select(l => $"{l.address}:{l.port}").ToArray()
				};
				status.IsFullySynched &= status.BitcoinStatus.IsSynched;
				status.ChainHeight = blockchainInfo.Headers;
			}
			status.SyncHeight = (int?)indexer.SyncHeight;
			status.IsFullySynched &= blockchainInfo != null
									&& indexer.State == BitcoinDWaiterState.Ready
									&& status.SyncHeight.HasValue
									&& blockchainInfo.Headers - status.SyncHeight.Value < 3;
			if (status.IsFullySynched)
			{
				var now = DateTimeOffset.UtcNow;
				var repo = RepositoryProvider.GetRepository(network);
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
				var network = GetNetwork(o.CryptoCode, false);
				if (network == null)
					return;

				bool forward = false;
				var derivationScheme = (o.TrackedSource as DerivationSchemeTrackedSource)?.DerivationStrategy;
				if (derivationScheme != null)
				{
					forward |= listenAllDerivationSchemes == "*" ||
								listenAllDerivationSchemes == o.CryptoCode ||
								listenedDerivations.ContainsKey((network.NBitcoinNetwork, derivationScheme));
				}

				forward |= listenAllTrackedSource == "*" || listenAllTrackedSource == o.CryptoCode ||
							listenedTrackedSource.ContainsKey((network.NBitcoinNetwork, o.TrackedSource));

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
							var network = GetNetwork(r.CryptoCode, false);
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
			var repo = RepositoryProvider.GetRepository(network);
			var result = await repo.GetSavedTransactions(txId);
			if (result.Length == 0)
			{
				var rpc = GetAvailableRPC(network);
				if (rpc is not null &&
					HasTxIndex(cryptoCode) &&
					await rpc.TryGetRawTransaction(txId) is SavedTransaction savedTransaction)
				{
					result = new[] { savedTransaction };
				}
				else
				{
					return NotFound();
				}
			}
			var tip = (await repo.GetTip()).Height;
			var tx = Utils.ToTransactionResult(tip, result);
			if (!includeTransaction)
				tx.Transaction = null;
			return Json(tx, network.Serializer.Settings);
		}

		private bool HasTxIndex(NBXplorerNetwork network)
		{
			return HasTxIndex(network.CryptoCode);
		}
		private bool HasTxIndex(string cryptoCode)
		{
			var chainConfig = this.ExplorerConfiguration.ChainConfigurations.FirstOrDefault(c => c.CryptoCode.Equals(cryptoCode.Trim(), StringComparison.OrdinalIgnoreCase));
			return chainConfig?.HasTxIndex is true;
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
			var repo = RepositoryProvider.GetRepository(network);

			var response = new GetTransactionsResponse();
			int currentHeight = (await repo.GetTip()).Height;
			response.Height = currentHeight;
			var txs = await GetAnnotatedTransactions(repo, trackedSource, includeTransaction, txId);
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
					new
					{
						TxSet = response.ImmatureTransactions,
						AnnotatedTx = txs.ImmatureTransactions
					},
				})
			{
				foreach (var tx in item.AnnotatedTx)
				{
					var txInfo = new TransactionInformation()
					{
						BlockHash = tx.Height.HasValue ? tx.Record.BlockHash : null,
						Height = tx.Height,
						IsMature = tx.IsMature,
						TransactionId = tx.Record.TransactionHash,
						Transaction = includeTransaction ? tx.Record.Transaction : null,
						Confirmations = tx.Height.HasValue ? currentHeight - tx.Height.Value + 1 : 0,
						Timestamp = tx.Record.FirstSeen,
						Inputs = tx.Record.SpentOutpoints.Select(o => txs.GetUTXO(o)).Where(o => o != null).ToList(),
						Outputs = tx.Record.GetReceivedOutputs().ToList(),
						Replaceable = tx.Replaceable,
						ReplacedBy = tx.ReplacedBy == NBXplorerNetwork.UnknownTxId ? null : tx.ReplacedBy,
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

			var rpc = RPCClients.Get(network).PrepareBatch();
			var repo = RepositoryProvider.GetRepository(network);
			var fetchingTransactions = rescanRequest
				.Transactions
				.Select(t => FetchTransaction(rpc, HasTxIndex(network), t))
				.ToArray();

			await rpc.SendBatchAsync();
			await Task.WhenAll(fetchingTransactions);

			var transactions = fetchingTransactions.Select(t => t.GetAwaiter().GetResult())
												   .Where(tx => tx.Transaction != null)
												   .ToArray();

			var blocks = new Dictionary<uint256, Task<SlimChainedBlock>>();
			var batch = rpc.PrepareBatch();
			foreach (var tx in transactions)
			{
				if (tx.BlockId != null && !blocks.ContainsKey(tx.BlockId))
				{
					blocks.Add(tx.BlockId, rpc.GetBlockHeaderAsyncEx(tx.BlockId));
				}
			}
			await batch.SendBatchAsync();
			await repo.SaveBlocks(blocks.Select(b => b.Value.Result).ToList());
			foreach (var txs in transactions.GroupBy(t => t.BlockId, t => (t.Transaction, t.BlockTime))
											.OrderBy(t => t.First().BlockTime))
			{
				blocks.TryGetValue(txs.Key, out var slimBlock);
				await repo.SaveTransactions(txs.First().BlockTime, txs.Select(t => t.Transaction).ToArray(), slimBlock.Result);
				foreach (var tx in txs)
				{
					var matches = await repo.GetMatches(tx.Transaction, slimBlock.Result, tx.BlockTime, false);
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
			var network = this.GetNetwork(cryptoCode, false);
			var trackedSource = new DerivationSchemeTrackedSource(derivationScheme);
			var repo = this.RepositoryProvider.GetRepository(network);
			var result = await repo.GetMetadata<JToken>(trackedSource, key);
			return result == null ? (IActionResult)NotFound() : Json(result, repo.Serializer.Settings);
		}
		Encoding UTF8 = new UTF8Encoding(false);


		[HttpPost]
		[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}/utxos/wipe")]
		public async Task<IActionResult> Wipe(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase derivationScheme)
		{
			var network = this.GetNetwork(cryptoCode, true);
			var repo = RepositoryProvider.GetRepository(network);
			var ts = new DerivationSchemeTrackedSource(derivationScheme);
			var txs = await repo.GetTransactions(ts);
			await repo.Prune(ts, txs);
			return Ok();
		}


		[HttpPost]
		[Route("cryptos/{cryptoCode}/derivations/{derivationScheme}/utxos/scan")]
		public IActionResult ScanUTXOSet(
			string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase derivationScheme, int? batchSize = null, int? gapLimit = null, int? from = null)
		{
			var network = this.GetNetwork(cryptoCode, true);
			var rpc = GetAvailableRPC(network);
			if (!rpc.Capabilities.SupportScanUTXOSet)
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
		[PostgresImplementationActionConstraint(false)]
		public async Task<IActionResult> GetBalance(string cryptoCode,
			[ModelBinder(BinderType = typeof(DerivationStrategyModelBinder))]
			DerivationStrategyBase derivationScheme,
			[ModelBinder(BinderType = typeof(BitcoinAddressModelBinder))]
			BitcoinAddress address)
		{
			var getTransactionsResult = await GetTransactions(cryptoCode, derivationScheme, address, includeTransaction: false);
			var jsonResult = getTransactionsResult as JsonResult;
			var transactions = jsonResult?.Value as GetTransactionsResponse;
			if (transactions == null)
				return getTransactionsResult;

			var network = this.GetNetwork(cryptoCode, false);
			var balance = new GetBalanceResponse()
			{
				Confirmed = CalculateBalance(network, transactions.ConfirmedTransactions),
				Unconfirmed = CalculateBalance(network, transactions.UnconfirmedTransactions),
				Immature = CalculateBalance(network, transactions.ImmatureTransactions)
			};
			balance.Total = balance.Confirmed.Add(balance.Unconfirmed);
			balance.Available = balance.Total.Sub(balance.Immature);
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
		[PostgresImplementationActionConstraint(false)]
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
			var repo = RepositoryProvider.GetRepository(network);

			changes = new UTXOChanges();
			changes.CurrentHeight = (await repo.GetTip()).Height;
			var transactions = await GetAnnotatedTransactions(repo, trackedSource, false);

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

		int MaxHeight = int.MaxValue;

		private void FillUTXOsInformation(List<UTXO> utxos, AnnotatedTransactionCollection transactions, int currentHeight)
		{
			for (int i = 0; i < utxos.Count; i++)
			{
				var utxo = utxos[i];
				utxo.KeyPath = transactions.GetKeyPath(utxo.ScriptPubKey);
				if (utxo.KeyPath != null)
					utxo.Feature = keyPathTemplates.GetDerivationFeature(utxo.KeyPath);
				var txHeight = transactions.GetByTxId(utxo.Outpoint.Hash).Height is long h ? h : MaxHeight;
				var isUnconf = txHeight == MaxHeight;
				utxo.Confirmations = isUnconf ? 0 : currentHeight - txHeight + 1;
				utxo.Timestamp = transactions.GetByTxId(utxo.Outpoint.Hash).Record.FirstSeen;
			}
		}

		private async Task<AnnotatedTransactionCollection> GetAnnotatedTransactions(IRepository repo, TrackedSource trackedSource, bool includeTransaction, uint256 txId = null)
		{
			var transactions = await repo.GetTransactions(trackedSource, txId, includeTransaction, this.HttpContext?.RequestAborted ?? default);

			// If the called is interested by only a single txId, we need to fetch the parents as well
			if (txId != null)
			{
				var spentOutpoints = transactions.SelectMany(t => t.SpentOutpoints.Select(o => o.Hash)).ToHashSet();
				var gettingParents = spentOutpoints.Select(async h => await repo.GetTransactions(trackedSource, h)).ToList();
				await Task.WhenAll(gettingParents);
				transactions = gettingParents.SelectMany(p => p.GetAwaiter().GetResult()).Concat(transactions).ToArray();
			}

			var annotatedTransactions = new AnnotatedTransactionCollection(transactions, trackedSource, repo.Network.NBitcoinNetwork);

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

			var rpc = GetAvailableRPC(network);
			if (testMempoolAccept && !rpc.Capabilities.SupportTestMempoolAccept)
				throw new NBXplorerException(new NBXplorerError(400, "not-supported", "This feature is not supported for this crypto currency"));
			var repo = RepositoryProvider.GetRepository(network);
			var indexer = Indexers.GetIndexer(network);
			RPCException rpcEx = null;
			try
			{
				if (testMempoolAccept)
				{
					var mempoolAccept = await rpc.TestMempoolAcceptAsync(tx, default);
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
				await rpc.SendRawTransactionAsync(tx);
				await indexer.SaveMatches(tx);
				return new BroadcastResult(true);
			}
			catch (RPCException ex) when (!testMempoolAccept)
			{
				rpcEx = ex;
				Logs.Explorer.LogInformation($"{network.CryptoCode}: Transaction {tx.GetHash()} failed to broadcast (Code: {ex.RPCCode}, Message: {ex.RPCCodeMessage}, Details: {ex.Message} )");
				if (trackedSource != null && ex.Message.StartsWith("Missing inputs", StringComparison.OrdinalIgnoreCase))
				{
					Logs.Explorer.LogInformation($"{network.CryptoCode}: Trying to broadcast unconfirmed of the wallet");
					var transactions = await GetAnnotatedTransactions(repo, trackedSource, true);
					foreach (var existing in transactions.UnconfirmedTransactions)
					{
						var t = existing.Record.Transaction ?? (await repo.GetSavedTransactions(existing.Record.TransactionHash)).Select(c => c.Transaction).FirstOrDefault();
						if (t == null)
							continue;
						try
						{
							await rpc.SendRawTransactionAsync(t);
						}
						catch { }
					}

					try
					{
						await rpc.SendRawTransactionAsync(tx);
						Logs.Explorer.LogInformation($"{network.CryptoCode}: Broadcast success");
						await indexer.SaveMatches(tx);
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
				"Transaction rejected by AcceptToMemoryPool" => RPCErrorCode.RPC_TRANSACTION_REJECTED,
				"AcceptToMemoryPool failed" => RPCErrorCode.RPC_TRANSACTION_REJECTED,
				"insufficient fee" => RPCErrorCode.RPC_TRANSACTION_REJECTED,
				_ => RPCErrorCode.RPC_TRANSACTION_ERROR
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
				ScriptPubKeyType = request.ScriptPubKeyType.Value,
				AdditionalOptions = request.AdditionalOptions
			});

			await RepositoryProvider.GetRepository(network).EnsureWalletCreated(derivation);
			var derivationTrackedSource = new DerivationSchemeTrackedSource(derivation);
			List<Task> saveMetadata = new List<Task>();
			if (request.SavePrivateKeys)
			{
				saveMetadata.AddRange(
				new[] {
					repo.SaveMetadata(derivationTrackedSource, WellknownMetadataKeys.Mnemonic, mnemonic.ToString()),
					repo.SaveMetadata(derivationTrackedSource, WellknownMetadataKeys.MasterHDKey, masterKey),
					repo.SaveMetadata(derivationTrackedSource, WellknownMetadataKeys.AccountHDKey, accountKey),
					repo.SaveMetadata(derivationTrackedSource, WellknownMetadataKeys.Birthdate, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))
				});
			}
			var accountKeyPath = new RootedKeyPath(masterKey.GetPublicKey().GetHDFingerPrint(), keyPath);
			saveMetadata.Add(repo.SaveMetadata(derivationTrackedSource, WellknownMetadataKeys.AccountKeyPath, accountKeyPath));

			var importAddressToRPC = await GetImportAddressToRPC(request, network);
			saveMetadata.Add(repo.SaveMetadata<string>(derivationTrackedSource, WellknownMetadataKeys.ImportAddressToRPC, importAddressToRPC.ToString()));
			var descriptor = GetDescriptor(accountKeyPath, accountKey.Neuter(), request.ScriptPubKeyType.Value);
			saveMetadata.Add(repo.SaveMetadata<string>(derivationTrackedSource, WellknownMetadataKeys.AccountDescriptor, descriptor));
			await Task.WhenAll(saveMetadata.ToArray());

			await TrackWallet(cryptoCode, derivation, null);
			return Json(new GenerateWalletResponse()
			{
				MasterHDKey = masterKey,
				AccountHDKey = accountKey,
				AccountKeyPath = accountKeyPath,
				AccountDescriptor = descriptor,
				DerivationScheme = derivation,
				Mnemonic = mnemonic.ToString(),
				Passphrase = request.Passphrase ?? string.Empty,
				WordCount = request.WordCount.Value,
				WordList = request.WordList
			}, network.Serializer.Settings);
		}

		private async Task<ImportRPCMode> GetImportAddressToRPC(GenerateWalletRequest request, NBXplorerNetwork network)
		{
			var importAddressToRPC = ImportRPCMode.Legacy;
			if (request.ImportKeysToRPC is true)
			{
				var rpc = this.GetAvailableRPC(network);
				try
				{
					var walletInfo = await rpc.SendCommandAsync("getwalletinfo");
					if (walletInfo.Result["descriptors"]?.Value<bool>() is true)
					{
						var readOnly = walletInfo.Result["private_keys_enabled"]?.Value<bool>() is false;
						importAddressToRPC = readOnly ? ImportRPCMode.DescriptorsReadOnly : ImportRPCMode.Descriptors;
						if (!readOnly && request.SavePrivateKeys is false)
							throw new NBXplorerError(400, "wallet-unavailable", $"Your RPC wallet must include private keys, but savePrivateKeys is false").AsException();
					}
				}
				catch (RPCException ex) when (ex.RPCCode == RPCErrorCode.RPC_METHOD_NOT_FOUND)
				{
				}
				catch (RPCException ex) when (ex.RPCCode == RPCErrorCode.RPC_WALLET_NOT_FOUND)
				{
					throw new NBXplorerError(400, "wallet-unavailable", $"No wallet is loaded. Load a wallet using loadwallet or create a new one with createwallet. (Note: A default wallet is no longer automatically created)").AsException();
				}
			}

			return importAddressToRPC;
		}

		private string GetDescriptor(RootedKeyPath accountKeyPath, BitcoinExtPubKey accountKey, ScriptPubKeyType scriptPubKeyType)
		{
			var imported = $"[{accountKeyPath}]{accountKey}";
			var descriptor = scriptPubKeyType switch
			{
				ScriptPubKeyType.Legacy => $"pkh({imported})",
				ScriptPubKeyType.Segwit => $"wpkh({imported})",
				ScriptPubKeyType.SegwitP2SH => $"sh(wpkh({imported}))",
				ScriptPubKeyType.TaprootBIP86 => $"tr({imported})",
				_ => throw new NotSupportedException($"Bug of NBXplorer (ERR 3082), please notify the developers ({scriptPubKeyType})")
			};
			return OutputDescriptor.AddChecksum(descriptor);
		}

		private KeyPath GetDerivationKeyPath(ScriptPubKeyType scriptPubKeyType, int accountNumber, NBXplorerNetwork network)
		{
			var path = "";
			switch (scriptPubKeyType)
			{
				case ScriptPubKeyType.Legacy:
					path = "44'";
					break;
				case ScriptPubKeyType.Segwit:
					path = "84'";
					break;
				case ScriptPubKeyType.SegwitP2SH:
					path = "49'";
					break;
				case ScriptPubKeyType.TaprootBIP86:
					path = "86'";
					break;
				default:
					throw new NotSupportedException(scriptPubKeyType.ToString()); // Should never happen
			}
			var keyPath = new KeyPath(path);
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
			var repo = RepositoryProvider.GetRepository(network);
			var transactions = await GetAnnotatedTransactions(repo, trackedSource, false);
			var state = transactions.ConfirmedState;
			var prunableIds = new HashSet<uint256>();

			var keepConfMax = network.NBitcoinNetwork.Consensus.GetExpectedBlocksFor(TimeSpan.FromDays(request.DaysToKeep.Value));
			var tip = (await repo.GetTip()).Height;
			// Step 1. We can prune if all UTXOs are spent
			foreach (var tx in transactions.ConfirmedTransactions)
			{
				if (tx.Height is long h && tip - h + 1 > keepConfMax)
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

		public Task<IActionResult> GetUTXOs(string cryptoCode, DerivationStrategyBase derivationStrategy)
		{
			return this.GetUTXOs(cryptoCode, derivationStrategy, null);
		}
	}
}
