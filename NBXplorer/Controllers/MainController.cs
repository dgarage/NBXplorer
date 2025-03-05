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
using System.Reflection;
using NBXplorer.Analytics;
using NBXplorer.Backend;
using static NBXplorer.Backend.DbConnectionHelper;
using static NBXplorer.ListenEventsRequest;
using NBXplorer.HostedServices;

namespace NBXplorer.Controllers
{
	[Route("v1")]
	[Authorize]
	public partial class MainController : Controller
	{
		public NBXplorerNetworkProvider NetworkProvider { get; }
		public RPCClientProvider RPCClients { get; }
		public RepositoryProvider RepositoryProvider { get; }
		public Indexers Indexers { get; }
		public CommonRoutesController CommonRoutesController { get; }
		public UTXOFetcherService UtxoFetcherService { get; }

		JsonSerializerSettings _SerializerSettings;
		public MainController(
			ExplorerConfiguration explorerConfiguration,
			RepositoryProvider repositoryProvider,
			EventAggregator eventAggregator,
			RPCClientProvider rpcClients,
			AddressPoolService addressPoolService,
			MvcNewtonsoftJsonOptions jsonOptions,
			NBXplorerNetworkProvider networkProvider,
			Analytics.FingerprintHostedService fingerprintService,
			Indexers indexers,
			CommonRoutesController commonRoutesController,
			UTXOFetcherService utxoFetcherService
			)
		{
			ExplorerConfiguration = explorerConfiguration;
			_SerializerSettings = jsonOptions.SerializerSettings;
			_EventAggregator = eventAggregator;
			this.fingerprintService = fingerprintService;
			AddressPoolService = addressPoolService;
			NetworkProvider = networkProvider;
			RPCClients = rpcClients;
			RepositoryProvider = repositoryProvider;
			Indexers = indexers;
			CommonRoutesController = commonRoutesController;
			UtxoFetcherService = utxoFetcherService;
		}
		EventAggregator _EventAggregator;
		private readonly FingerprintHostedService fingerprintService;

		public AddressPoolService AddressPoolService
		{
			get;
		}
		public ExplorerConfiguration ExplorerConfiguration { get; }


		static HashSet<string> WhitelistedRPCMethods = new HashSet<string>()
		{
			"sendrawtransaction",
			"getrawtransaction",
			"gettxout",
			"estimatesmartfee",
			"getmempoolinfo",
			"getmempoolentry",
			"gettxoutproof",
			"verifytxoutproof",
			"getblockchaininfo",
			"getblockhash",
			"getblockheader"
		};
		internal NBXplorerNetwork GetNetwork(string cryptoCode, bool checkRPC)
		{
			if (cryptoCode == null)
				throw new ArgumentNullException(nameof(cryptoCode));
			cryptoCode = cryptoCode.ToUpperInvariant();
			var network = NetworkProvider.GetFromCryptoCode(cryptoCode);
			if (network == null || Indexers.GetIndexer(network) is null)
				throw new NBXplorerException(new NBXplorerError(404, "cryptoCode-not-supported", $"{cryptoCode} is not supported"));

			if (checkRPC)
			{
				var rpc = GetAvailableRPC(network);
				if (rpc is null || rpc.Capabilities == null)
					throw new NBXplorerError(400, "rpc-unavailable", $"The RPC interface is currently not available.").AsException();
			}
			return network;
		}
		protected RPCClient GetAvailableRPC(NBXplorerNetwork network)
		{
			return Indexers.GetIndexer(network)?.GetConnectedClient();
		}
		private Exception JsonRPCNotExposed()
		{
			return new NBXplorerError(401, "json-rpc-not-exposed", $"JSON-RPC is not configured to be exposed. Only the following methods are available: {string.Join(", ", WhitelistedRPCMethods)}").AsException();
		}

		[HttpPost]
		[Route($"{CommonRoutes.BaseCryptoEndpoint}/rpc")]
		[Consumes("application/json", "application/json-rpc")]
		public async Task<IActionResult> RPCProxy(string cryptoCode)
		{
			var network = GetNetwork(cryptoCode, true);
			var exposed = ExplorerConfiguration.ChainConfigurations.First(configuration => configuration.CryptoCode.Equals(cryptoCode, StringComparison.InvariantCultureIgnoreCase)).ExposeRPC;
			var rpc = RPCClients.Get(network);
			var jsonRPC = string.Empty;
			using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
			{
				jsonRPC = await reader.ReadToEndAsync();
			}

			var req = RPCProxyRequest.TryParse(jsonRPC);
			if (req is RPCProxyRequest.RPCProxyBatchedRequest batch)
			{
				var batchRPC = rpc.PrepareBatch();
				if (batch.Requests.Count is 0)
					return Json(new JArray());
				var authorized = batch.Requests.All(r => WhitelistedRPCMethods.Contains(r.Method));
				if (!exposed && !authorized)
					throw JsonRPCNotExposed();
				var results = batch.Requests.Select(r => batchRPC.SendCommandAsync(r)).ToList();
				await batchRPC.SendBatchAsync();
				return Json(new JArray(results.Select(task => ToRPCResponse(task.Result))));
			}
			else if (req is RPCProxyRequest.RPCProxySingleRequest sr)
			{
				var authorized = WhitelistedRPCMethods.Contains(sr.Request.Method);
				if (!exposed && !authorized)
					throw JsonRPCNotExposed();
				var result = ToRPCResponse(await rpc.SendCommandAsync(sr.Request));
				return Json(result);
			}
			else
			{
				throw new NBXplorerError(422, "no-json-rpc-request", $"A JSON-RPC request was not provided in the body.").AsException();
			}
		}

		private JObject ToRPCResponse(RPCResponse result) => result switch
		{
			{ Error: RPCError error } => new JObject
			{
				{ "error", new JObject
					{
						{ "code", (int)error.Code },
						{ "message", error.Message }
					}
				}
			},
			{ Result: JToken rpcResult } => new JObject
			{
				{ "result", rpcResult }
			},
			_ => throw new InvalidOperationException("Unknown result from RPC")
		};

		[HttpGet]
		[Route($"{CommonRoutes.BaseCryptoEndpoint}/fees/{{blockCount}}")]
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
		[Route($"{CommonRoutes.DerivationEndpoint}/addresses/unused")]
		[TrackedSourceContext.TrackedSourceContextRequirement(allowedTrackedSourceTypes: typeof(DerivationSchemeTrackedSource))]
		public async Task<IActionResult> GetUnusedAddress(
			TrackedSourceContext trackedSourceContext, DerivationFeature feature = DerivationFeature.Deposit, int skip = 0, bool reserve = false, bool autoTrack = false)
		{
			var strategy = ((DerivationSchemeTrackedSource)trackedSourceContext.TrackedSource).DerivationStrategy;
			var network = trackedSourceContext.Network;
			var repository = trackedSourceContext.Repository;
			if (skip >= repository.MinPoolSize)
				throw new NBXplorerError(404, "strategy-not-found", $"This strategy is not tracked, or you tried to skip too much unused addresses").AsException();
			try
			{
				var result = await repository.GetUnused(strategy, feature, skip, reserve);
				if (reserve || autoTrack)
				{
					while (result == null)
					{
						await AddressPoolService.GenerateAddresses(network, strategy, feature, new GenerateAddressQuery(1, null));
						result = await repository.GetUnused(strategy, feature, skip, reserve);
					}
					if (reserve)
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
		[Route($"{CommonRoutes.DerivationEndpoint}/addresses/cancelreservation")]
		[TrackedSourceContext.TrackedSourceContextRequirement(allowedTrackedSourceTypes: typeof(DerivationSchemeTrackedSource))]
		public async Task<IActionResult> CancelReservation(TrackedSourceContext trackedSourceContext, [FromBody] KeyPath[] keyPaths)
		{
			var repo = trackedSourceContext.Repository;
			var strategy = ((DerivationSchemeTrackedSource)trackedSourceContext.TrackedSource).DerivationStrategy;
			await repo.CancelReservation(strategy, keyPaths);
			return Ok();
		}

		[HttpGet]
		[Route($"{CommonRoutes.BaseCryptoEndpoint}/scripts/{{script}}")]
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
		[Route($"{CommonRoutes.DerivationEndpoint}/scripts/{{script}}")]
		[TrackedSourceContext.TrackedSourceContextRequirement(allowedTrackedSourceTypes: typeof(DerivationSchemeTrackedSource))]
		public async Task<IActionResult> GetKeyInformations(TrackedSourceContext trackedSourceContext, [ModelBinder(BinderType = typeof(ScriptModelBinder))] Script script)
		{
			var network = trackedSourceContext.Network;
			var repo = trackedSourceContext.Repository;
			var strategy = ((DerivationSchemeTrackedSource)trackedSourceContext.TrackedSource).DerivationStrategy;
			var result = (await repo.GetKeyInformations(new[] { script }))
				.SelectMany(k => k.Value)
				.FirstOrDefault(k => k.DerivationStrategy == strategy);
			if (result == null)
				throw new NBXplorerError(404, "script-not-found", "The script does not seem to be tracked").AsException();
			return Json(result, network.Serializer.Settings);
		}

		[HttpGet]
		[Route($"{CommonRoutes.BaseCryptoEndpoint}/status")]
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
			return Json(status, network.Serializer.Settings);
		}

		[HttpGet]
		[Route($"{CommonRoutes.BaseCryptoEndpoint}/connect")]
		public Task<IActionResult> ConnectWebSocketLegacy(
			string cryptoCode,
			CancellationToken cancellation = default)
		=> ConnectWebSocketCore(new ListenEventsRequest()
		{
			DefaultAction = RuleAction.Reject,
			Rules = []
		}, cryptoCode, cancellation);

		[HttpGet]
		[Route($"cryptos/connect")]
		public Task<IActionResult> ConnectWebSocket(
			string cryptoCode = null,
			CancellationToken cancellation = default)
		=> ConnectWebSocketCore(new ListenEventsRequest()
		{
			DefaultAction = RuleAction.Allow,
			Rules = cryptoCode is (null or "*") ? []
					: [new() { CryptoCode = cryptoCode, Action = RuleAction.Reject, Inverse = true }]
		}, cryptoCode, cancellation);

		[NonAction]
		async Task<IActionResult> ConnectWebSocketCore(
			ListenEventsRequest policy,
			string defaultCryptoCode,
			CancellationToken cancellation = default)
		{
			if (!HttpContext.WebSockets.IsWebSocketRequest)
				return NotFound();

			WebsocketMessageListener server = new WebsocketMessageListener(await HttpContext.WebSockets.AcceptWebSocketAsync(), _SerializerSettings);
			async Task ForwardEvent<T>(T o, string cryptoCode) where T : NewEventBase
			{
				var action = policy.DefaultAction;
				foreach (var rule in policy.Rules)
				{
					Func<bool, bool> maybeInverse = rule.Inverse ? a => !a : a => a;
					if (rule.CryptoCode is not (null or "*"))
					{
						if (maybeInverse(rule.CryptoCode != cryptoCode))
							continue;
					}
					var typeMatch = (rule.Type, o) switch
					{
						(null, _) => true,
						(ListenRule.EventType.NewBlock, Models.NewBlockEvent) => maybeInverse(true),
						(ListenRule.EventType.NewTransaction, Models.NewTransactionEvent) => maybeInverse(true),
						_ => false
					};
					if (!typeMatch)
						continue;
					if (o is Models.NewTransactionEvent nte)
					{
						if (rule.TrackedSource is { } trackedSource)
						{
							if (maybeInverse(trackedSource != nte.TrackedSource.ToString()))
								continue;
						}
						if (rule.DerivationScheme is { } derivationScheme)
						{
							if (maybeInverse(derivationScheme != (nte.TrackedSource as DerivationSchemeTrackedSource)?.DerivationStrategy?.ToString()))
								continue;
						}
					}
					action = rule.Action;
					break;
				}

				if (action == RuleAction.Allow)
				{
					await server.Send(o, GetSerializerSettings(cryptoCode));
				}
			}

			CompositeDisposable subscriptions = new CompositeDisposable();
			subscriptions.Add(_EventAggregator.Subscribe<Models.NewBlockEvent>(o => ForwardEvent(o, o.CryptoCode)));
			subscriptions.Add(_EventAggregator.Subscribe<Models.NewTransactionEvent>(o => ForwardEvent(o, o.CryptoCode)));
			try
			{
				while (server.Socket.State == WebSocketState.Open)
				{
					var rules = policy.Rules.ToList();
					object message = await server.NextMessageAsync(cancellation);
					switch (message)
					{
						case Models.NewBlockEventRequest r:
							r.CryptoCode ??= defaultCryptoCode;
							rules.Add(new ListenRule()
							{
								CryptoCode = r.CryptoCode,
								Type = ListenRule.EventType.NewBlock,
								Action = RuleAction.Allow
							});
							break;
						case Models.NewTransactionEventRequest r:
							r.CryptoCode ??= defaultCryptoCode;
							if (r.DerivationSchemes != null)
							{
								foreach (var derivation in r.DerivationSchemes)
								{
									rules.Add(new()
									{
										CryptoCode = r.CryptoCode,
										Type = ListenRule.EventType.NewTransaction,
										DerivationScheme = derivation
									});
								}
							}
							else if (
								// Back compat: If no derivation scheme precised and ListenAllDerivationSchemes not set, we listen all
								(r.TrackedSources == null && r.ListenAllDerivationSchemes == null) ||
								(r.ListenAllDerivationSchemes is true))
							{
								rules.Add(new()
								{
									CryptoCode = r.CryptoCode,
									Type = ListenRule.EventType.NewTransaction
								});
							}

							if (r.ListenAllTrackedSource is true)
							{
								rules.Add(new()
								{
									CryptoCode = r.CryptoCode,
									Type = ListenRule.EventType.NewTransaction
								});
							}
							else if (r.TrackedSources != null)
							{
								foreach (var trackedSource in r.TrackedSources)
								{
									rules.Add(new()
									{
										CryptoCode = r.CryptoCode,
										Type = ListenRule.EventType.NewTransaction,
										TrackedSource = trackedSource
									});
								}
							}
							break;
						default:
							break;
					}
					policy.Rules = rules.ToArray();
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

		[Route($"{CommonRoutes.BaseCryptoEndpoint}/events")]
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


		[Route($"{CommonRoutes.BaseCryptoEndpoint}/events/latest")]
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
		[Route($"{CommonRoutes.BaseCryptoEndpoint}/transactions/{{txId}}")]
		public async Task<IActionResult> GetTransaction(
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 txId,
			bool includeTransaction = true,
			string cryptoCode = null, CancellationToken cancellationToken = default)
		{
			var network = GetNetwork(cryptoCode, false);
			var rpc = GetAvailableRPC(network);
			var repo = RepositoryProvider.GetRepository(network);
			var result = await repo.GetSavedTransaction(txId);
			if (result is not null && result.Transaction is null)
			{
				var dummy = Transaction.Create(network.NBitcoinNetwork);
				dummy.Inputs.Add(new OutPoint(txId, 0));
				var update = new UpdatePSBTRequest()
				{
					PSBT = PSBT.FromTransaction(dummy, network.NBitcoinNetwork),
					AlwaysIncludeNonWitnessUTXO = true
				};
				await this.UtxoFetcherService.UpdateUTXO(update);
				result.Transaction = (await UtxoFetcherService.FetchTransactions([txId], network)).Select(kv => kv.Value).FirstOrDefault();
				if (result.Transaction is null)
					return null;
			}
			else if (result is null)
			{
				if (rpc is not null &&
					await rpc.TryGetRawTransaction(txId, cancellationToken) is SavedTransaction savedTransaction)
				{
					result = savedTransaction;
					if (result.BlockHash is null)
					{
						try
						{
							var entry = await rpc.GetMempoolEntryAsync(txId, false, cancellationToken);
							if (result.Metadata is null && entry is not null)
								result.Metadata = entry.ToTransactionMetadata();
						}
						// Not essential data, we can ignore if we can't get it
						catch { }
					}
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

		[HttpGet]
		[Route($"{CommonRoutes.DerivationEndpoint}/{CommonRoutes.TransactionsPath}")]
		[Route($"{CommonRoutes.AddressEndpoint}/{CommonRoutes.TransactionsPath}")]
		[Route($"{CommonRoutes.BaseCryptoEndpoint}/{CommonRoutes.GroupEndpoint}/{CommonRoutes.TransactionsPath}")]
		public async Task<IActionResult> GetTransactions(
			TrackedSourceContext trackedSourceContext,
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 txId = null,
			bool includeTransaction = true,
			[ModelBinder(BinderType = typeof(DateTimeOffsetModelBinder))]
			DateTimeOffset? from = null,
			[ModelBinder(BinderType = typeof(DateTimeOffsetModelBinder))]
			DateTimeOffset? to = null)
		{
			TransactionInformation fetchedTransactionInfo = null;
			var network = trackedSourceContext.Network;
			var repo = RepositoryProvider.GetRepository(trackedSourceContext.Network);
			var trackedSource = trackedSourceContext.TrackedSource;

			var response = new GetTransactionsResponse();
			int currentHeight = (await repo.GetTip()).Height;
			response.Height = currentHeight;
			var query = GetTransactionQuery.Create(trackedSource, txId, from, to);
			var txs = await GetAnnotatedTransactions(repo, query, includeTransaction);
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
						Inputs = tx.Record.MatchedInputs,
						Outputs = tx.Record.MatchedOutputs,
						Replaceable = tx.Replaceable,
						Metadata = tx.Record.Metadata,
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
		[Route($"{CommonRoutes.BaseCryptoEndpoint}/rescan")]
		[TrackedSourceContext.TrackedSourceContextRequirement(false, false, true)]
		public async Task<IActionResult> Rescan(TrackedSourceContext trackedSourceContext, [FromBody] JObject body, CancellationToken cancellationToken = default)
		{
			if (body == null)
				throw new ArgumentNullException(nameof(body));
			var network = trackedSourceContext.Network;
			var rescanRequest = network.ParseJObject<RescanRequest>(body);
			if (rescanRequest == null)
				throw new ArgumentNullException(nameof(rescanRequest));
			if (rescanRequest?.Transactions == null)
				throw new NBXplorerException(new NBXplorerError(400, "transactions-missing", "You must specify 'transactions'"));

			bool willFetchTransactions = rescanRequest.Transactions.Any(t => t.Transaction == null);
			if (willFetchTransactions && trackedSourceContext.RpcClient is null)
			{
				TrackedSourceContext.TrackedSourceContextModelBinder.ThrowRpcUnavailableException();
			}
			var repo = trackedSourceContext.Repository;
			var rpc = trackedSourceContext.RpcClient!.PrepareBatch();
			var fetchingTransactions = rescanRequest
				.Transactions
				.Select(t => FetchTransaction(rpc, network, t))
				.ToArray();

			await rpc.SendBatchAsync();
			await Task.WhenAll(fetchingTransactions);

			var transactions = fetchingTransactions.Select(t => t.GetAwaiter().GetResult())
												   .Where(tx => tx.Transaction != null)
												   .ToArray();

			var blocks = await rpc.GetBlockHeadersAsync(transactions.Select(t => t.BlockId).Where(b => b != null).ToArray(), cancellationToken);
			await repo.SaveBlocks(blocks);
			foreach (var txs in transactions.GroupBy(t => t.BlockId, t => (t.Transaction, t.BlockTime))
											.OrderBy(t => t.First().BlockTime))
			{
				blocks.ByHashes.TryGetValue(txs.Key, out var b);
				var slimBlock = b?.ToSlimChainedBlock();
				var records = txs.Select(t => SaveTransactionRecord.Create(t.Transaction, slimBlock: slimBlock, seenAt: t.BlockTime)).ToArray();
				var query = MatchQuery.FromTransactions(records.Select(r => r.Transaction), repo.MinUtxoValue);
				var matches = await repo.SaveMatches(query, records);
				_ = AddressPoolService.GenerateAddresses(network, matches);
			}
			return Ok();
		}

		async Task<(uint256 BlockId, Transaction Transaction, DateTimeOffset BlockTime)> FetchTransaction(RPCClient rpc, NBXplorerNetwork network, RescanRequest.TransactionToRescan transaction)
		{
			var hasTxIndex = this.Indexers.GetIndexer(network)?.HasTxIndex is true;
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
		[Route($"{CommonRoutes.BaseCryptoEndpoint}/rescan-utxos")]
		[TrackedSourceContext.TrackedSourceContextRequirement(false, false, true)]
		public async Task<IActionResult> ImportUTXOs(TrackedSourceContext trackedSourceContext, [FromBody] ImportUTXORequest request, CancellationToken cancellationToken = default)
		{
			var repo = trackedSourceContext.Repository;
			if (request.Utxos?.Any() is not true)
				return Ok();

			var rpc = trackedSourceContext.RpcClient;

			var coinToTxOut = await rpc.GetTxOuts(request.Utxos);
			var bestBlocksToFetch = coinToTxOut.Select(c => c.Value.BestBlock).ToHashSet().ToList();
			var bestBlocks = await rpc.GetBlockHeadersAsync(bestBlocksToFetch, cancellationToken);
			var coinsWithHeights = coinToTxOut
				.Select(c => new
				{
					BestBlock = bestBlocks.ByHashes.TryGet(c.Value.BestBlock),
					Outpoint = c.Key,
					RPCTxOut = c.Value
				})
				.Select(c => new
				{
					Height = c.RPCTxOut.Confirmations == 0 ? null : new int?(c.BestBlock.Height - c.RPCTxOut.Confirmations + 1),
					c.Outpoint,
					c.RPCTxOut
				})
				.ToList();
			var blocks = coinsWithHeights.Where(c => c.Height.HasValue).Select(c => c.Height.Value).Distinct().ToList();
			var blockHeaders = await rpc.GetBlockHeadersAsync(blocks, cancellationToken);

			var now = DateTimeOffset.UtcNow;
			var records = new List<SaveTransactionRecord>();
			MatchQuery query = new MatchQuery(coinsWithHeights.Select(c => new Coin(c.Outpoint, c.RPCTxOut.TxOut)));
			foreach (var c in coinsWithHeights)
			{
				var block = c.Height is int h ? blockHeaders.ByHeight.TryGet(h) : null;
				var record = SaveTransactionRecord.Create(
									txHash: c.Outpoint.Hash,
									slimBlock: block?.ToSlimChainedBlock(),
									seenAt: Extensions.MinDate(block?.Time ?? now, now));
				records.Add(record);
			}
			await repo.SaveBlocks(blockHeaders);
			repo.RemoveFromCache(records.Select(r => r.Id));
			var trackedTransactions = await repo.SaveMatches(query, records.ToArray());
			_ = AddressPoolService.GenerateAddresses(trackedSourceContext.Network, trackedTransactions);

			return Ok();
		}

		internal async Task<AnnotatedTransactionCollection> GetAnnotatedTransactions(Repository repo, GetTransactionQuery.TrackedSourceTxId query, bool includeTransaction)
		{
			var transactions = await repo.GetTransactions(query, includeTransaction, this.HttpContext?.RequestAborted ?? default);

			// If the called is interested by only a single txId, we need to fetch the parents as well
			if (query.TxId != null)
			{
				var spentOutpoints = transactions.SelectMany(t => t.SpentOutpoints.Select(o => o.Outpoint.Hash)).ToHashSet();
				var gettingParents = spentOutpoints.Select(async h => await repo.GetTransactions(GetTransactionQuery.Create(query.TrackedSource, h))).ToList();
				await Task.WhenAll(gettingParents);
				transactions = gettingParents.SelectMany(p => p.GetAwaiter().GetResult()).Concat(transactions).ToArray();
			}

			return new AnnotatedTransactionCollection(transactions, query.TrackedSource, repo.Network.NBitcoinNetwork);
		}

		[HttpPost]
		[Route($"{CommonRoutes.BaseCryptoEndpoint}/transactions")]
		[TrackedSourceContext.TrackedSourceContextRequirement(true, false)]
		public async Task<BroadcastResult> Broadcast(
			TrackedSourceContext trackedSourceContext,
			bool testMempoolAccept = false)
		{
			var network = trackedSourceContext.Network;
			var tx = network.NBitcoinNetwork.Consensus.ConsensusFactory.CreateTransaction();
			var buffer = new MemoryStream();
			await Request.Body.CopyToAsync(buffer);
			buffer.Position = 0;
			tx.FromBytes(buffer.ToArrayEfficient());

			if (testMempoolAccept && !trackedSourceContext.RpcClient.Capabilities.SupportTestMempoolAccept)
				throw new NBXplorerException(new NBXplorerError(400, "not-supported", "This feature is not supported for this crypto currency"));
			var repo = RepositoryProvider.GetRepository(network);
			RPCException rpcEx = null;
			try
			{
				if (testMempoolAccept)
				{
					var mempoolAccept = await trackedSourceContext.RpcClient.TestMempoolAcceptAsync(tx, default);
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
				await trackedSourceContext.RpcClient.SendRawTransactionAsync(tx);
				await trackedSourceContext.Indexer.SaveMatches(tx);
				return new BroadcastResult(true);
			}
			catch (RPCException ex) when (!testMempoolAccept)
			{
				rpcEx = ex;
				Logs.Explorer.LogInformation($"{network.CryptoCode}: Transaction {tx.GetHash()} failed to broadcast (Code: {ex.RPCCode}, Message: {ex.RPCCodeMessage}, Details: {ex.Message} )");
				if (trackedSourceContext.TrackedSource != null && ex.Message.StartsWith("Missing inputs", StringComparison.OrdinalIgnoreCase))
				{
					Logs.Explorer.LogInformation($"{network.CryptoCode}: Trying to broadcast unconfirmed of the wallet");
					var transactions = await GetAnnotatedTransactions(repo, GetTransactionQuery.Create(trackedSourceContext.TrackedSource), true);
					foreach (var existing in transactions.UnconfirmedTransactions)
					{
						var t = existing.Record.Transaction ?? (await repo.GetSavedTransaction(existing.Record.TransactionHash))?.Transaction;
						if (t == null)
							continue;
						try
						{
							await trackedSourceContext.RpcClient.SendRawTransactionAsync(t);
						}
						catch { }
					}

					try
					{
						await trackedSourceContext.RpcClient.SendRawTransactionAsync(tx);
						Logs.Explorer.LogInformation($"{network.CryptoCode}: Broadcast success");
						await trackedSourceContext.Indexer.SaveMatches(tx);
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
				"Transaction already in block chain" or "Transaction outputs already in utxo set" => RPCErrorCode.RPC_VERIFY_ALREADY_IN_CHAIN,
				"Transaction rejected by AcceptToMemoryPool" => RPCErrorCode.RPC_TRANSACTION_REJECTED,
				"AcceptToMemoryPool failed" => RPCErrorCode.RPC_TRANSACTION_REJECTED,
				"insufficient fee" => RPCErrorCode.RPC_TRANSACTION_REJECTED,
				_ => RPCErrorCode.RPC_TRANSACTION_ERROR
			};
		}
	}
}
