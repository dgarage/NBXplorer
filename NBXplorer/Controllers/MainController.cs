﻿using NBXplorer.Logging;
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
using NBXplorer.Backends.Postgres;

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
			) : base(networkProvider, rpcClients, repositoryProvider, indexers)
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

		static HashSet<string> WhitelistedRPCMethods = new HashSet<string>()
		{
			"sendrawtransaction",
			"getrawtransaction",
			"gettxout",
			"estimatesmartfee",
			"getmempoolinfo",
			"gettxoutproof",
			"verifytxoutproof"
		};
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

			if (string.IsNullOrEmpty(jsonRPC))
			{
				throw new NBXplorerError(422, "no-json-rpc-request", $"A JSON-RPC request was not provided in the body.").AsException();
			}
			if (jsonRPC.StartsWith("["))
			{
				var batchRPC = rpc.PrepareBatch();
				var results = network.Serializer.ToObject<RPCRequest[]>(jsonRPC).Select(rpcRequest =>
				{
					if (!exposed && !WhitelistedRPCMethods.Contains(rpcRequest.Method))
						throw JsonRPCNotExposed();
					rpcRequest.ThrowIfRPCError = false;
					return batchRPC.SendCommandAsync(rpcRequest);
				}).ToList();
				await batchRPC.SendBatchAsync();
				return Json(results.Select(task => task.Result));
			}

			var req = network.Serializer.ToObject<RPCRequest>(jsonRPC);
			if (!exposed && !WhitelistedRPCMethods.Contains(req.Method))
				throw JsonRPCNotExposed();
			req.ThrowIfRPCError = false;
			return Json(await rpc.SendCommandAsync(req));
		}

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
			var derivationScheme = ((DerivationSchemeTrackedSource ) trackedSourceContext.TrackedSource).DerivationStrategy;
			var network = trackedSourceContext.Network;
			var repository = trackedSourceContext.Repository;
			if (skip >= repository.MinPoolSize)
				throw new NBXplorerError(404, "strategy-not-found", $"This strategy is not tracked, or you tried to skip too much unused addresses").AsException();
			try
			{
				var result = await repository.GetUnused(derivationScheme, feature, skip, reserve);
				if (reserve || autoTrack)
				{
					while (result == null)
					{
						await AddressPoolService.GenerateAddresses(network, derivationScheme, feature, new GenerateAddressQuery(1, null));
						result = await repository.GetUnused(derivationScheme, feature, skip, reserve);
					}
					if (reserve)
						_ = AddressPoolService.GenerateAddresses(network, derivationScheme, feature);
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
		[TrackedSourceContext.TrackedSourceContextRequirement( allowedTrackedSourceTypes:typeof(DerivationSchemeTrackedSource))]
		public async Task<IActionResult> CancelReservation(TrackedSourceContext trackedSourceContext, [FromBody] KeyPath[] keyPaths)
		{
			var derivationScheme = ((DerivationSchemeTrackedSource ) trackedSourceContext.TrackedSource).DerivationStrategy;
			await trackedSourceContext.Repository.CancelReservation(derivationScheme, keyPaths);
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
		[Route($"{CommonRoutes.AddressEndpoint}/scripts/{{script}}")]
		[Route($"{CommonRoutes.WalletEndpoint}/scripts/{{script}}")]
		[Route($"{CommonRoutes.TrackedSourceEndpoint}/scripts/{{script}}")]
		public async Task<IActionResult> GetKeyInformations(TrackedSourceContext trackedSourceContext, [ModelBinder(BinderType = typeof(ScriptModelBinder))] Script script)
		{
			var result = (await trackedSourceContext.Repository.GetKeyInformations(new[] { script }))
				.SelectMany(k => k.Value)
				.FirstOrDefault(k => k.TrackedSource == trackedSourceContext.TrackedSource);
			if (result == null)
				throw new NBXplorerError(404, "script-not-found", "The script does not seem to be tracked").AsException();
			return Json(result, trackedSourceContext.Network.Serializer.Settings);
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
		[Route($"{CommonRoutes.BaseCryptoEndpoint}/connect")]
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
		[Route($"{CommonRoutes.DerivationEndpoint}")]
		[Route($"{CommonRoutes.AddressEndpoint}")]
		[Route($"{CommonRoutes.WalletEndpoint}")]
		[Route($"{CommonRoutes.TrackedSourceEndpoint}")]
		public async Task<IActionResult> TrackWallet(
			TrackedSourceContext trackedSourceContext,
			[FromBody] JObject rawRequest = null)
		{
			var request = trackedSourceContext.Network.ParseJObject<TrackWalletRequest>(rawRequest ?? new JObject());
			
			var repo = RepositoryProvider.GetRepository(trackedSourceContext.Network);
			if (repo is PostgresRepository postgresRepository && 
			    (trackedSourceContext.TrackedSource is WalletTrackedSource || 
			     request?.ParentWallet is not null))
			{
				if (request?.ParentWallet == trackedSourceContext.TrackedSource)
				{
					throw new NBXplorerException(new NBXplorerError(400, "parent-wallet-same-as-tracked-source",
						"Parent wallets cannot be the same as the tracked source"));
				}
				await postgresRepository.EnsureWalletCreated(trackedSourceContext.TrackedSource, request?.ParentWallet);
			}
			if (repo is not PostgresRepository && request.ParentWallet is not null)
				throw new NBXplorerException(new NBXplorerError(400, "parent-wallet-not-supported",
					"Parent wallet is only supported with Postgres"));
			if (trackedSourceContext.TrackedSource is DerivationSchemeTrackedSource dts)
			{
				if (request.Wait)
				{
					foreach (var feature in keyPathTemplates.GetSupportedDerivationFeatures())
					{
						await RepositoryProvider.GetRepository(trackedSourceContext.Network).GenerateAddresses(dts.DerivationStrategy, feature, GenerateAddressQuery(request, feature));
					}
				}
				else
				{
					foreach (var feature in keyPathTemplates.GetSupportedDerivationFeatures())
					{
						await repo.GenerateAddresses(dts.DerivationStrategy, feature, new GenerateAddressQuery(minAddresses: 3, null));
					}
					foreach (var feature in keyPathTemplates.GetSupportedDerivationFeatures())
					{
						_ = AddressPoolService.GenerateAddresses(trackedSourceContext.Network, dts.DerivationStrategy, feature, GenerateAddressQuery(request, feature));
					}
				}
			}
			else if (trackedSourceContext.TrackedSource is IDestination ats)
			{
				await repo.Track(ats);
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
		
		
		[Route($"{CommonRoutes.DerivationEndpoint}/{CommonRoutes.TransactionsPath}")]
		[Route($"{CommonRoutes.AddressEndpoint}/{CommonRoutes.TransactionsPath}")]
		[Route($"{CommonRoutes.WalletEndpoint}/{CommonRoutes.TransactionsPath}")]
		[Route($"{CommonRoutes.TrackedSourceEndpoint}/{CommonRoutes.TransactionsPath}")]
		
		public async Task<IActionResult> GetTransactions(
			TrackedSourceContext trackedSourceContext,
			[ModelBinder(BinderType = typeof(UInt256ModelBinding))]
			uint256 txId = null,
			bool includeTransaction = true)
		{
			TransactionInformation fetchedTransactionInfo = null;
			var repo = trackedSourceContext.Repository;
			var response = new GetTransactionsResponse();
			int currentHeight = (await repo.GetTip()).Height;
			response.Height = currentHeight;
			var txs = await GetAnnotatedTransactions(repo, trackedSourceContext.TrackedSource, includeTransaction, txId);
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
						Inputs = tx.Record.SpentOutpoints.Select(o =>txs.GetSpentUTXO(o.Outpoint, o.InputIndex)).Where(o => o != null).ToList(),
						Outputs = tx.Record.GetReceivedOutputs().ToList(),
						Replaceable = tx.Replaceable,
						ReplacedBy = tx.ReplacedBy == NBXplorerNetwork.UnknownTxId ? null : tx.ReplacedBy,
						Replacing = tx.Replacing
					};

					if (txId == null || txId == txInfo.TransactionId)
						item.TxSet.Transactions.Add(txInfo);
					if (txId != null && txId == txInfo.TransactionId)
						fetchedTransactionInfo = txInfo;

					if (trackedSourceContext.Network.NBitcoinNetwork.NetworkSet == NBitcoin.Altcoins.Liquid.Instance)
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
		public async Task<IActionResult> Rescan(TrackedSourceContext trackedSourceContext, [FromBody] JObject body)
		{
			if (body == null)
				throw new ArgumentNullException(nameof(body));
			var rescanRequest = trackedSourceContext.Network.ParseJObject<RescanRequest>(body);
			if (rescanRequest == null)
				throw new ArgumentNullException(nameof(rescanRequest));
			if (rescanRequest?.Transactions == null)
				throw new NBXplorerException(new NBXplorerError(400, "transactions-missing", "You must specify 'transactions'"));

			bool willFetchTransactions = rescanRequest.Transactions.Any(t => t.Transaction == null);
			if (willFetchTransactions && trackedSourceContext.RpcClient is null)
			{
				TrackedSourceContext.TrackedSourceContextModelBinder.ThrowRpcUnavailableException();
			}

			var rpc = trackedSourceContext.RpcClient!.PrepareBatch();
			var fetchingTransactions = rescanRequest
				.Transactions
				.Select(t => FetchTransaction(rpc, HasTxIndex(trackedSourceContext.Network), t))
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
			await trackedSourceContext.Repository.SaveBlocks(blocks.Select(b => b.Value.Result).ToList());
			foreach (var txs in transactions.GroupBy(t => t.BlockId, t => (t.Transaction, t.BlockTime))
											.OrderBy(t => t.First().BlockTime))
			{
				blocks.TryGetValue(txs.Key, out var slimBlock);
				await trackedSourceContext.Repository.SaveTransactions(txs.First().BlockTime, txs.Select(t => t.Transaction).ToArray(), slimBlock.Result);
				foreach (var tx in txs)
				{
					var matches = await trackedSourceContext.Repository.GetMatches(tx.Transaction, slimBlock.Result, tx.BlockTime, false);
					await trackedSourceContext.Repository.SaveMatches(matches);
					_ = AddressPoolService.GenerateAddresses(trackedSourceContext.Network, matches);
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
		[Route($"{CommonRoutes.DerivationEndpoint}/metadata/{{key}}")]
		public async Task<IActionResult> SetMetadata(TrackedSourceContext trackedSourceContext, string key, [FromBody] JToken value = null)
		{
			await trackedSourceContext.Repository.SaveMetadata(trackedSourceContext.TrackedSource, key, value);
			return Ok();
		}
		[HttpGet]
		[Route($"{CommonRoutes.DerivationEndpoint}/metadata/{{key}}")]
		public async Task<IActionResult> GetMetadata(TrackedSourceContext trackedSourceContext, string key)
		{
			var result = await trackedSourceContext.Repository.GetMetadata<JToken>(trackedSourceContext.TrackedSource, key);
			return result == null ? NotFound() : Json(result, trackedSourceContext.Repository.Serializer.Settings);
		}


		[HttpPost]
		[Route($"{CommonRoutes.DerivationEndpoint}/utxos/wipe")]
		public async Task<IActionResult> Wipe(TrackedSourceContext trackedSourceContext)
		{
			var txs = await trackedSourceContext.Repository.GetTransactions(trackedSourceContext.TrackedSource);
			await trackedSourceContext.Repository.Prune(trackedSourceContext.TrackedSource, txs);
			return Ok();
		}


		[HttpPost]
		[Route($"{CommonRoutes.DerivationEndpoint}/utxos/scan")]
		[TrackedSourceContext.TrackedSourceContextRequirement(requireRPC:true,allowedTrackedSourceTypes: new []{typeof(DerivationSchemeTrackedSource)})]
		public IActionResult ScanUTXOSet(TrackedSourceContext trackedSourceContext, int? batchSize = null, int? gapLimit = null, int? from = null)
		{
			if (!trackedSourceContext.RpcClient.Capabilities.SupportScanUTXOSet)
				throw new NBXplorerError(405, "scanutxoset-not-suported", "ScanUTXOSet is not supported for this currency").AsException();

			ScanUTXOSetOptions options = new ScanUTXOSetOptions();
			if (batchSize != null)
				options.BatchSize = batchSize.Value;
			if (gapLimit != null)
				options.GapLimit = gapLimit.Value;
			if (from != null)
				options.From = from.Value;
			if (!ScanUTXOSetService.EnqueueScan(trackedSourceContext.Network, ((DerivationSchemeTrackedSource) trackedSourceContext.TrackedSource).DerivationStrategy, options))
				throw new NBXplorerError(409, "scanutxoset-in-progress", "ScanUTXOSet has already been called for this derivationScheme").AsException();
			return Ok();
		}

		[HttpGet]
		[Route($"{CommonRoutes.DerivationEndpoint}/utxos/scan")]
		[TrackedSourceContext.TrackedSourceContextRequirement(allowedTrackedSourceTypes: new []{typeof(DerivationSchemeTrackedSource)})]
		public IActionResult GetScanUTXOSetInfromation(TrackedSourceContext trackedSourceContext)
		{
			var info = ScanUTXOSetService.GetInformation(trackedSourceContext.Network, ((DerivationSchemeTrackedSource) trackedSourceContext.TrackedSource).DerivationStrategy);
			if (info == null)
				throw new NBXplorerError(404, "scanutxoset-info-not-found", "ScanUTXOSet has not been called with this derivationScheme of the result has expired").AsException();
			return Json(info, trackedSourceContext.Network.Serializer.Settings);
		}
#if SUPPORT_DBTRIE
		[HttpGet]
		[Route($"{CommonRoutes.DerivationEndpoint}/balance")]
		[Route($"{CommonRoutes.AddressEndpoint}/balance")]
		[TrackedSourceContext.TrackedSourceContextRequirement(allowedTrackedSourceTypes: new []{typeof(DerivationSchemeTrackedSource),typeof(AddressTrackedSource)})]
		[PostgresImplementationActionConstraint(false)]
		public async Task<IActionResult> GetBalance(TrackedSourceContext trackedSourceContext)
		{
			var getTransactionsResult = await GetTransactions(trackedSourceContext, includeTransaction: false);
			var jsonResult = getTransactionsResult as JsonResult;
			var transactions = jsonResult?.Value as GetTransactionsResponse;
			if (transactions == null)
				return getTransactionsResult;

			var balance = new GetBalanceResponse()
			{
				Confirmed = CalculateBalance(trackedSourceContext.Network, transactions.ConfirmedTransactions),
				Unconfirmed = CalculateBalance(trackedSourceContext.Network, transactions.UnconfirmedTransactions),
				Immature = CalculateBalance(trackedSourceContext.Network, transactions.ImmatureTransactions)
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
		[Route($"{CommonRoutes.DerivationEndpoint}/utxos")]
		[Route($"{CommonRoutes.AddressEndpoint}/utxos")]
		[PostgresImplementationActionConstraint(false)]
		[TrackedSourceContext.TrackedSourceContextRequirement(allowedTrackedSourceTypes: new []{typeof(DerivationSchemeTrackedSource),typeof(AddressTrackedSource)})]
		public async Task<IActionResult> GetUTXOs(TrackedSourceContext trackedSourceContext)
		{
			UTXOChanges changes = null;

			var repo = RepositoryProvider.GetRepository(trackedSourceContext.Network);

			changes = new UTXOChanges();
			changes.CurrentHeight = (await repo.GetTip()).Height;
			var transactions = await GetAnnotatedTransactions(repo, trackedSourceContext.TrackedSource, false);

			changes.Confirmed = ToUTXOChange(transactions.ConfirmedState);
			changes.Confirmed.SpentOutpoints.Clear();
			changes.Unconfirmed = ToUTXOChange(transactions.UnconfirmedState - transactions.ConfirmedState);

			FillUTXOsInformation(changes.Confirmed.UTXOs, transactions, changes.CurrentHeight);
			FillUTXOsInformation(changes.Unconfirmed.UTXOs, transactions, changes.CurrentHeight);

			changes.TrackedSource = trackedSourceContext.TrackedSource;
			changes.DerivationStrategy = (trackedSourceContext.TrackedSource as DerivationSchemeTrackedSource)?.DerivationStrategy;

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
#endif
		private async Task<AnnotatedTransactionCollection> GetAnnotatedTransactions(IRepository repo, TrackedSource trackedSource, bool includeTransaction, uint256 txId = null)
		{
			var transactions = await repo.GetTransactions(trackedSource, txId, includeTransaction, this.HttpContext?.RequestAborted ?? default);

			// If the called is interested by only a single txId, we need to fetch the parents as well
			if (txId != null)
			{
				var spentOutpoints = transactions.SelectMany(t => t.SpentOutpoints.Select(o => o.Outpoint.Hash)).ToHashSet();
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
		[Route($"{CommonRoutes.BaseCryptoEndpoint}/transactions")]
		[TrackedSourceContext.TrackedSourceContextRequirement(true, false)]
		public async Task<BroadcastResult> Broadcast(
			TrackedSourceContext trackedSourceContext,
			bool testMempoolAccept = false)
		{
			
			var tx = trackedSourceContext.Network.NBitcoinNetwork.Consensus.ConsensusFactory.CreateTransaction();
			var buffer = new MemoryStream();
			await Request.Body.CopyToAsync(buffer);
			buffer.Position = 0;
			tx.FromBytes(buffer.ToArrayEfficient());

			if (testMempoolAccept && !trackedSourceContext.RpcClient.Capabilities.SupportTestMempoolAccept)
				throw new NBXplorerException(new NBXplorerError(400, "not-supported", "This feature is not supported for this crypto currency"));
			var repo = RepositoryProvider.GetRepository(trackedSourceContext.Network);
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
				Logs.Explorer.LogInformation($"{trackedSourceContext.Network.CryptoCode}: Transaction {tx.GetHash()} failed to broadcast (Code: {ex.RPCCode}, Message: {ex.RPCCodeMessage}, Details: {ex.Message} )");
				if (trackedSourceContext.TrackedSource != null && ex.Message.StartsWith("Missing inputs", StringComparison.OrdinalIgnoreCase))
				{
					Logs.Explorer.LogInformation($"{trackedSourceContext.Network.CryptoCode}: Trying to broadcast unconfirmed of the wallet");
					var transactions = await GetAnnotatedTransactions(repo, trackedSourceContext.TrackedSource, true);
					foreach (var existing in transactions.UnconfirmedTransactions)
					{
						var t = existing.Record.Transaction ?? (await repo.GetSavedTransactions(existing.Record.TransactionHash)).Select(c => c.Transaction).FirstOrDefault();
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
						Logs.Explorer.LogInformation($"{trackedSourceContext.Network.CryptoCode}: Broadcast success");
						await trackedSourceContext.Indexer.SaveMatches(tx);
						return new BroadcastResult(true);
					}
					catch (RPCException)
					{
						Logs.Explorer.LogInformation($"{trackedSourceContext.Network.CryptoCode}: Transaction {tx.GetHash()} failed to broadcast (Code: {ex.RPCCode}, Message: {ex.RPCCodeMessage}, Details: {ex.Message} )");
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
		
		[Route($"{CommonRoutes.BaseDerivationEndpoint}")]
		[TrackedSourceContext.TrackedSourceContextRequirement(false, false)]
		public async Task<IActionResult> GenerateWallet(TrackedSourceContext trackedSourceContext, [FromBody] JObject rawRequest = null)
		{
			var request = trackedSourceContext.Network.ParseJObject<GenerateWalletRequest>(rawRequest ) ?? new GenerateWalletRequest();

			if (request.ImportKeysToRPC && trackedSourceContext.RpcClient is null)
			{
				TrackedSourceContext.TrackedSourceContextModelBinder.ThrowRpcUnavailableException();
			}
			if (trackedSourceContext.Network.CoinType == null)
				// Don't document, only shitcoins nobody use goes into this
				throw new NBXplorerException(new NBXplorerError(400, "not-supported", "This feature is not supported for this coin because we don't have CoinType information"));
			request.WordList ??= Wordlist.English;
			request.WordCount ??= WordCount.Twelve;
			request.ScriptPubKeyType ??= ScriptPubKeyType.Segwit;
			if (request.ScriptPubKeyType is null)
			{
				request.ScriptPubKeyType = trackedSourceContext.Network.NBitcoinNetwork.Consensus.SupportSegwit ? ScriptPubKeyType.Segwit : ScriptPubKeyType.Legacy;
			}
			if (!trackedSourceContext.Network.NBitcoinNetwork.Consensus.SupportSegwit && request.ScriptPubKeyType != ScriptPubKeyType.Legacy)
				throw new NBXplorerException(new NBXplorerError(400, "segwit-not-supported", "Segwit is not supported, please explicitely set scriptPubKeyType to Legacy"));

			var repo = RepositoryProvider.GetRepository(trackedSourceContext.Network);
			if (repo is not PostgresRepository && request.ParentWallet is not null)
				throw new NBXplorerException(new NBXplorerError(400, "parent-wallet-not-supported",
					"Parent wallet is only supported with Postgres"));
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
			var masterKey = mnemonic.DeriveExtKey(request.Passphrase).GetWif(trackedSourceContext.Network.NBitcoinNetwork);
			var keyPath = GetDerivationKeyPath(request.ScriptPubKeyType.Value, request.AccountNumber, trackedSourceContext.Network);
			var accountKey = masterKey.Derive(keyPath);
			DerivationStrategyBase derivation = trackedSourceContext.Network.DerivationStrategyFactory.CreateDirectDerivationStrategy(accountKey.Neuter(), new DerivationStrategyOptions()
			{
				ScriptPubKeyType = request.ScriptPubKeyType.Value,
				AdditionalOptions = request.AdditionalOptions is not null ? new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(request.AdditionalOptions) : null
			});
			if (request.ParentWallet is not null && repo is PostgresRepository postgresRepository)
			{
				await postgresRepository.EnsureWalletCreated(TrackedSource.Create(derivation), new[] {request.ParentWallet});
			}
			else
			{
				await repo.EnsureWalletCreated(derivation);
			}
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
			var importAddressToRPC = await GetImportAddressToRPC(request, trackedSourceContext.Network);
			saveMetadata.Add(repo.SaveMetadata<string>(derivationTrackedSource, WellknownMetadataKeys.ImportAddressToRPC, (importAddressToRPC?.ToString() ?? "False")));
			var descriptor = GetDescriptor(accountKeyPath, accountKey.Neuter(), request.ScriptPubKeyType.Value);
			saveMetadata.Add(repo.SaveMetadata<string>(derivationTrackedSource, WellknownMetadataKeys.AccountDescriptor, descriptor));
			await Task.WhenAll(saveMetadata.ToArray());

			
			await TrackWallet(new TrackedSourceContext()
			{
				Indexer = trackedSourceContext.Indexer,
				Network = trackedSourceContext.Network,
				RpcClient = trackedSourceContext.RpcClient,
				TrackedSource =  new DerivationSchemeTrackedSource(derivation)
			});
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
			}, trackedSourceContext.Network.Serializer.Settings);
		}

		private async Task<ImportRPCMode> GetImportAddressToRPC(GenerateWalletRequest request, NBXplorerNetwork network)
		{
			ImportRPCMode importAddressToRPC = null;
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
					else
					{
						importAddressToRPC = ImportRPCMode.Legacy;
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
		[Route($"{CommonRoutes.DerivationEndpoint}/prune")]
		[TrackedSourceContext.TrackedSourceContextRequirement(allowedTrackedSourceTypes: new []{typeof(DerivationSchemeTrackedSource)})]
		public async Task<PruneResponse> Prune(TrackedSourceContext trackedSourceContext ,[FromBody] PruneRequest request)
		{
			request ??= new PruneRequest();
			request.DaysToKeep ??= 1.0;
			var transactions = await GetAnnotatedTransactions(trackedSourceContext.Repository, trackedSourceContext.TrackedSource, false);
			var state = transactions.ConfirmedState;
			var prunableIds = new HashSet<uint256>();

			var keepConfMax = trackedSourceContext.Network.NBitcoinNetwork.Consensus.GetExpectedBlocksFor(TimeSpan.FromDays(request.DaysToKeep.Value));
			var tip = (await trackedSourceContext.Repository.GetTip()).Height;
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
													.Select(spent => transactions.GetByTxId(spent.Outpoint.Hash))
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
				await trackedSourceContext.Repository.Prune(trackedSourceContext.TrackedSource, prunableIds
												.Select(id => transactions.GetByTxId(id).Record)
												.ToList());
				Logs.Explorer.LogInformation($"{trackedSourceContext.Network.CryptoCode}: Pruned {prunableIds.Count} transactions");
			}
			return new PruneResponse() { TotalPruned = prunableIds.Count };
		}
#if !SUPPORT_DBTRIE
		public async Task<IActionResult> GetUTXOs(TrackedSourceContext trackedSourceContext)
		{
			throw new NotImplementedException();
		}
#endif
	}
}
