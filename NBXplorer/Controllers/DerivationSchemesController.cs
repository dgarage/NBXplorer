using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.Backend;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Globalization;
using System;
using System.Threading.Tasks;
using NBitcoin.RPC;
using NBitcoin.Scripting;
using System.Linq;
using NBXplorer.Logging;
using Microsoft.Extensions.Logging;

namespace NBXplorer.Controllers
{
	[Route($"v1/{CommonRoutes.BaseDerivationEndpoint}")]
	public class DerivationSchemesController : Controller
	{
		public ScanUTXOSetService ScanUTXOSetService { get; }
		public MainController MainController { get; }
		public RepositoryProvider RepositoryProvider { get; }
		public KeyPathTemplates KeyPathTemplates { get; }
		public Indexers Indexers { get; }
		public AddressPoolService AddressPoolService { get; }
		public DerivationSchemesController(
			MainController mainController,
			ScanUTXOSetServiceAccessor scanUTXOSetService,
			RepositoryProvider repositoryProvider,
			KeyPathTemplates keyPathTemplates,
			Indexers indexers,
			AddressPoolService addressPoolService)
		{
			ScanUTXOSetService = scanUTXOSetService.Instance;
			MainController = mainController;
			RepositoryProvider = repositoryProvider;
			KeyPathTemplates = keyPathTemplates;
			Indexers = indexers;
			AddressPoolService = addressPoolService;
		}

		[HttpPost($"~/v1/{CommonRoutes.DerivationEndpoint}")]
		[HttpPost($"~/v1/{CommonRoutes.AddressEndpoint}")]
		public async Task<IActionResult> TrackWallet(
			TrackedSourceContext trackedSourceContext,
			[FromBody] JObject rawRequest = null)
		{
			var network = trackedSourceContext.Network;
			var trackedSource = trackedSourceContext.TrackedSource;
			var request = network.ParseJObject<TrackWalletRequest>(rawRequest ?? new JObject());

			if (trackedSource is DerivationSchemeTrackedSource dts)
			{
				if (request.Wait)
				{
					foreach (var feature in KeyPathTemplates.GetSupportedDerivationFeatures())
					{
						await RepositoryProvider.GetRepository(network).GenerateAddresses(dts.DerivationStrategy, feature, GenerateAddressQuery(request, feature));
					}
				}
				else
				{
					foreach (var feature in KeyPathTemplates.GetSupportedDerivationFeatures())
					{
						await RepositoryProvider.GetRepository(network).GenerateAddresses(dts.DerivationStrategy, feature, new GenerateAddressQuery(minAddresses: 3, null));
					}
					foreach (var feature in KeyPathTemplates.GetSupportedDerivationFeatures())
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

		[HttpPost($"~/v1/{CommonRoutes.DerivationEndpoint}/utxos/wipe")]
		public async Task<IActionResult> Wipe(TrackedSourceContext trackedSourceContext)
		{
			var repo = trackedSourceContext.Repository;
			var ts = trackedSourceContext.TrackedSource;
			var txs = await repo.GetTransactions(trackedSourceContext.TrackedSource);
			await repo.Prune(txs);
			return Ok();
		}

		[HttpPost("utxos/scan")]
		[HttpPost($"~/v1/{CommonRoutes.DerivationEndpoint}/utxos/scan")]
		[TrackedSourceContext.TrackedSourceContextRequirement(requireRPC: true, allowedTrackedSourceTypes: typeof(DerivationSchemeTrackedSource))]
		public IActionResult ScanUTXOSet(TrackedSourceContext trackedSourceContext, int? batchSize = null, int? gapLimit = null, int? from = null)
		{
			var network = trackedSourceContext.Network;
			var rpc = trackedSourceContext.RpcClient;
			var derivationScheme = ((DerivationSchemeTrackedSource)trackedSourceContext.TrackedSource).DerivationStrategy;
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

		[HttpGet($"~/v1/{CommonRoutes.DerivationEndpoint}/utxos/scan")]
		[TrackedSourceContext.TrackedSourceContextRequirement(allowedTrackedSourceTypes: typeof(DerivationSchemeTrackedSource))]
		public IActionResult GetScanUTXOSetInformation(TrackedSourceContext trackedSourceContext)
		{
			var network = trackedSourceContext.Network;
			var derivationScheme = ((DerivationSchemeTrackedSource)trackedSourceContext.TrackedSource).DerivationStrategy;
			var info = ScanUTXOSetService.GetInformation(network, derivationScheme);
			if (info == null)
				throw new NBXplorerError(404, "scanutxoset-info-not-found", "ScanUTXOSet has not been called with this derivationScheme of the result has expired").AsException();
			return Json(info, network.Serializer.Settings);
		}

		[HttpPost($"~/v1/{CommonRoutes.DerivationEndpoint}/prune")]
		[TrackedSourceContext.TrackedSourceContextRequirement(allowedTrackedSourceTypes: [typeof(DerivationSchemeTrackedSource)])]
		public async Task<PruneResponse> Prune(TrackedSourceContext trackedSourceContext, [FromBody] PruneRequest request)
		{
			request ??= new PruneRequest();
			request.DaysToKeep ??= 1.0;
			var trackedSource = trackedSourceContext.TrackedSource;
			var network = trackedSourceContext.Network;
			var repo = trackedSourceContext.Repository;

			var transactions = await MainController.GetAnnotatedTransactions(repo, trackedSource, false);
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
				await repo.Prune(prunableIds
								.Select(id => transactions.GetByTxId(id).Record)
								.ToList());
				Logs.Explorer.LogInformation($"{network.CryptoCode}: Pruned {prunableIds.Count} transactions");
			}
			return new PruneResponse() { TotalPruned = prunableIds.Count };
		}
		[HttpPost]
		[TrackedSourceContext.TrackedSourceContextRequirement(false, false)]
		public async Task<IActionResult> GenerateWallet(TrackedSourceContext trackedSourceContext, [FromBody] JObject rawRequest = null)
		{
			var network = trackedSourceContext.Network;
			var request = network.ParseJObject<GenerateWalletRequest>(rawRequest) ?? new GenerateWalletRequest();

			if (request.ImportKeysToRPC && trackedSourceContext.RpcClient is null)
			{
				TrackedSourceContext.TrackedSourceContextModelBinder.ThrowRpcUnavailableException();
			}
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
				AdditionalOptions = request.AdditionalOptions is not null ? new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(request.AdditionalOptions) : null
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
			saveMetadata.Add(repo.SaveMetadata<string>(derivationTrackedSource, WellknownMetadataKeys.ImportAddressToRPC, (importAddressToRPC?.ToString() ?? "False")));
			var descriptor = GetDescriptor(accountKeyPath, accountKey.Neuter(), request.ScriptPubKeyType.Value);
			saveMetadata.Add(repo.SaveMetadata<string>(derivationTrackedSource, WellknownMetadataKeys.AccountDescriptor, descriptor));
			await Task.WhenAll(saveMetadata.ToArray());

			await TrackWallet(new TrackedSourceContext()
			{
				Indexer = trackedSourceContext.Indexer,
				Network = network,
				RpcClient = trackedSourceContext.RpcClient,
				TrackedSource = new DerivationSchemeTrackedSource(derivation)
			});
			return Json(new GenerateWalletResponse()
			{
				TrackedSource = new DerivationSchemeTrackedSource(derivation).ToString(),
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
		private async Task<ImportRPCMode> GetImportAddressToRPC(GenerateWalletRequest request, NBXplorerNetwork network)
		{
			ImportRPCMode importAddressToRPC = null;
			if (request.ImportKeysToRPC is true)
			{
				var rpc = Indexers.GetIndexer(network)?.GetConnectedClient();;
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
	}
}
