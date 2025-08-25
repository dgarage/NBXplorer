using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using NBitcoin.RPC;
using System.Runtime.CompilerServices;
using System.Linq;

namespace NBXplorer
{
	public class ExplorerClient
	{
		public interface IAuth
		{
			bool RefreshCache();
			void SetAuthorization(HttpRequestMessage message);
			void SetWebSocketAuth(ClientWebSocket socket);
		}

		class CookieAuthentication : IAuth
		{
			string _CookieFilePath;
			AuthenticationHeaderValue _CachedAuth;

			public CookieAuthentication(string path)
			{
				_CookieFilePath = path;
			}

			public string CookieFilePath => _CookieFilePath;

			public bool RefreshCache()
			{
				try
				{
					var cookieData = File.ReadAllText(CookieFilePath);
					_CachedAuth = new AuthenticationHeaderValue("Basic", Encoders.Base64.EncodeData(Encoders.ASCII.DecodeData(cookieData)));
					return true;
				}
				catch { return false; }
			}

			public void SetAuthorization(HttpRequestMessage message)
			{
				message.Headers.Authorization = _CachedAuth;
			}

			public void SetWebSocketAuth(ClientWebSocket socket)
			{
				if (_CachedAuth != null)
					socket.Options.SetRequestHeader("Authorization", $"{_CachedAuth.Scheme} {_CachedAuth.Parameter}");
			}
		}
		class NullAuthentication : IAuth
		{
			public bool RefreshCache()
			{
				return false;
			}

			public void SetAuthorization(HttpRequestMessage message)
			{
			}

			public void SetWebSocketAuth(ClientWebSocket socket)
			{
			}
		}

		public ExplorerClient(NBXplorerNetwork network, Uri serverAddress = null) : this(network, serverAddress, null)
		{

		}
		public ExplorerClient(NBXplorerNetwork network, Uri serverAddress, IAuth customAuth)
		{
			serverAddress = serverAddress ?? network.DefaultSettings.DefaultUrl;
			if (network == null)
				throw new ArgumentNullException(nameof(network));
			_Address = serverAddress;
			_Network = network;
			Serializer = new Serializer(network);
			_CryptoCode = _Network.CryptoCode;
			_Factory = Network.DerivationStrategyFactory;
			if (customAuth == null)
			{
				SetCookieAuth(network.DefaultSettings.DefaultCookieFile);
			}
			else
			{
				_Auth = customAuth;
				customAuth.RefreshCache();
			}
		}

		public RPCClient RPCClient { get; private set; }

		internal IAuth _Auth = new NullAuthentication();

		public bool SetCookieAuth(string path)
		{
			if (path == null)
				throw new ArgumentNullException(nameof(path));
			CookieAuthentication auth = new CookieAuthentication(path);
			Auth = auth;
			return auth.RefreshCache();
		}

		public void SetNoAuth()
		{
			Auth = new NullAuthentication();
		}

		private void ConstructRPCClient()
		{
			RPCClient = new RPCClient(Auth is CookieAuthentication cookieAuthentication
				? new RPCCredentialString()
				{
					CookieFile = cookieAuthentication.CookieFilePath
				}
				: new RPCCredentialString(), GetFullUri($"v1/cryptos/{_CryptoCode}/rpc"), _Network.NBitcoinNetwork);
		}

		private readonly string _CryptoCode = "BTC";
		public string CryptoCode
		{
			get
			{
				return _CryptoCode;
			}
		}

		DerivationStrategy.DerivationStrategyFactory _Factory;
		public UTXOChanges GetUTXOs(DerivationStrategyBase extKey, CancellationToken cancellation = default)
		{
			return GetUTXOsAsync(extKey, cancellation).GetAwaiter().GetResult();
		}
		public Task<UTXOChanges> GetUTXOsAsync(DerivationStrategyBase extKey, CancellationToken cancellation = default)
		{
			if (extKey == null)
				throw new ArgumentNullException(nameof(extKey));
			return GetUTXOsAsync(TrackedSource.Create(extKey), cancellation);
		}
		public async Task<TransactionResult> GetTransactionAsync(uint256 txId, CancellationToken cancellation = default)
		{
			return await SendAsync<TransactionResult>(HttpMethod.Get, null, $"v1/cryptos/{CryptoCode}/transactions/{txId}", cancellation).ConfigureAwait(false);
		}

		public TransactionResult GetTransaction(uint256 txId, CancellationToken cancellation = default)
		{
			return GetTransactionAsync(txId, cancellation).GetAwaiter().GetResult();
		}

		public async Task<PruneResponse> PruneAsync(DerivationStrategyBase extKey, PruneRequest pruneRequest, CancellationToken cancellation = default)
		{
			if (extKey == null)
				throw new ArgumentNullException(nameof(extKey));
			return await SendAsync<PruneResponse>(HttpMethod.Post, pruneRequest, $"v1/cryptos/{CryptoCode}/derivations/{extKey}/prune", cancellation).ConfigureAwait(false);
		}

		public PruneResponse Prune(DerivationStrategyBase extKey, PruneRequest pruneRequest, CancellationToken cancellation = default)
		{
			return PruneAsync(extKey, pruneRequest, cancellation).GetAwaiter().GetResult();
		}

		internal class RawStr
		{
			private string str;
			public RawStr(string str)
			{
				this.str = str;
			}
			public override string ToString() => str;
		};
		internal static RawStr Raw(string str) => new RawStr(str);

		public async Task ScanUTXOSetAsync(DerivationStrategyBase extKey, int? batchSize = null, int? gapLimit = null,
			int? fromIndex = null, CancellationToken cancellation = default)
		{
			await ScanUTXOSetAsync(new DerivationSchemeTrackedSource(extKey), batchSize, gapLimit, fromIndex,
				cancellation);
		}
		public async Task ScanUTXOSetAsync(TrackedSource trackedSource, int? batchSize = null, int? gapLimit = null, int? fromIndex = null, CancellationToken cancellation = default)
		{
			if (trackedSource == null)
				throw new ArgumentNullException(nameof(trackedSource));
			List<string> args = new List<string>();
			if (batchSize != null)
				args.Add($"batchsize={batchSize.Value}");
			if (gapLimit != null)
				args.Add($"gaplimit={gapLimit.Value}");
			if (fromIndex != null)
				args.Add($"from={fromIndex.Value}");
			var argsString = string.Join("&", args.ToArray());
			if (argsString != string.Empty)
				argsString = $"?{argsString}";
			
			
			await SendAsync<bool>(HttpMethod.Post, null, $"{GetBasePath(trackedSource)}/utxos/scan{Raw(argsString)}", cancellation).ConfigureAwait(false);
		}
		public void ScanUTXOSet(DerivationStrategyBase extKey, int? batchSize = null, int? gapLimit = null, int? fromIndex = null, CancellationToken cancellation = default)
		{
			ScanUTXOSetAsync(extKey, batchSize, gapLimit, fromIndex, cancellation).GetAwaiter().GetResult();
		}

		public async Task<ScanUTXOInformation> GetScanUTXOSetInformationAsync(DerivationStrategyBase extKey,
			CancellationToken cancellation = default)
		{
			return await GetScanUTXOSetInformationAsync(new DerivationSchemeTrackedSource(extKey), cancellation);
		}

		public async Task<ScanUTXOInformation> GetScanUTXOSetInformationAsync(TrackedSource trackedSource, CancellationToken cancellation = default)
		{
			return await SendAsync<ScanUTXOInformation>(HttpMethod.Get, null, $"{GetBasePath(trackedSource)}/utxos/scan", cancellation).ConfigureAwait(false);
		}

		public ScanUTXOInformation GetScanUTXOSetInformation(DerivationStrategyBase extKey, CancellationToken cancellation = default)
		{
			return GetScanUTXOSetInformationAsync(extKey, cancellation).GetAwaiter().GetResult();
		}

		public LongPollingNotificationSession CreateLongPollingNotificationSession(long lastEventId = 0)
		{
			return new LongPollingNotificationSession(lastEventId, this);
		}

		public WebsocketNotificationSession CreateWebsocketNotificationSession(CancellationToken cancellation = default)
		{
			return CreateWebsocketNotificationSessionAsync(cancellation).GetAwaiter().GetResult();
		}

		public async Task<WebsocketNotificationSession> CreateWebsocketNotificationSessionAsync(CancellationToken cancellation = default)
		{
			var session = new WebsocketNotificationSession(this);
			await session.ConnectAsync(cancellation).ConfigureAwait(false);
			return session;
		}
		public WebsocketNotificationSessionLegacy CreateWebsocketNotificationSessionLegacy(CancellationToken cancellation = default)
		{
			return CreateWebsocketNotificationSessionLegacyAsync(cancellation).GetAwaiter().GetResult();
		}

		public async Task<WebsocketNotificationSessionLegacy> CreateWebsocketNotificationSessionLegacyAsync(CancellationToken cancellation = default)
		{
			var session = new WebsocketNotificationSessionLegacy(this);
			await session.ConnectAsync(cancellation).ConfigureAwait(false);
			return session;
		}

		public UTXOChanges GetUTXOs(TrackedSource trackedSource, CancellationToken cancellation = default)
		{
			return GetUTXOsAsync(trackedSource, cancellation).GetAwaiter().GetResult();
		}
		public Task<UTXOChanges> GetUTXOsAsync(TrackedSource trackedSource, CancellationToken cancellation = default)
		{

			if (trackedSource == null)
				throw new ArgumentNullException(nameof(trackedSource));
			return SendAsync<UTXOChanges>(HttpMethod.Get, null, $"{GetBasePath(trackedSource)}/utxos", cancellation);
		}

		public void WaitServerStarted(CancellationToken cancellation = default)
		{
			WaitServerStartedAsync(cancellation).GetAwaiter().GetResult();
		}
		public async Task WaitServerStartedAsync(CancellationToken cancellation = default)
		{
			while (true)
			{
				try
				{
					var status = await GetStatusAsync(cancellation).ConfigureAwait(false);
					if (status.IsFullySynched)
						break;
					await Task.Delay(10);
				}
				catch (OperationCanceledException) { throw; }
				catch { }
				cancellation.ThrowIfCancellationRequested();
			}
		}


		public void Track(DerivationStrategyBase strategy, CancellationToken cancellation = default)
		{
			TrackAsync(strategy, cancellation).GetAwaiter().GetResult();
		}
		public Task TrackAsync(DerivationStrategyBase strategy, CancellationToken cancellation = default)
		{
			return TrackAsync(TrackedSource.Create(strategy), cancellation: cancellation);
		}

		public void Track(DerivationStrategyBase strategy, TrackWalletRequest trackDerivationRequest, CancellationToken cancellation = default)
		{
			TrackAsync(strategy, trackDerivationRequest, cancellation).GetAwaiter().GetResult();
		}
		public async Task TrackAsync(DerivationStrategyBase strategy, TrackWalletRequest trackDerivationRequest, CancellationToken cancellation = default)
		{
			if (strategy == null)
				throw new ArgumentNullException(nameof(strategy));
			await SendAsync<string>(HttpMethod.Post, trackDerivationRequest, $"v1/cryptos/{CryptoCode}/derivations/{strategy}", cancellation).ConfigureAwait(false);
		}

		public void Track(TrackedSource trackedSource, CancellationToken cancellation = default)
		{
			TrackAsync(trackedSource, cancellation: cancellation).GetAwaiter().GetResult();
		}
		public Task TrackAsync(TrackedSource trackedSource, TrackWalletRequest trackDerivationRequest = null, CancellationToken cancellation = default)
		{
			if (trackedSource == null)
				throw new ArgumentNullException(nameof(trackedSource));
			return SendAsync<string>(HttpMethod.Post, trackDerivationRequest, GetBasePath(trackedSource), cancellation);
		}

		private Exception UnSupported(TrackedSource trackedSource)
		{
			return new NotSupportedException($"Unsupported {trackedSource.GetType().Name}");
		}

		public void CancelReservation(DerivationStrategyBase strategy, KeyPath[] keyPaths, CancellationToken cancellation = default)
		{
			CancelReservationAsync(strategy, keyPaths, cancellation).GetAwaiter().GetResult();
		}

		public GetBalanceResponse GetBalance(DerivationStrategyBase userDerivationScheme, CancellationToken cancellation = default)
		{
			return GetBalanceAsync(userDerivationScheme, cancellation).GetAwaiter().GetResult();
		}
		public Task<GetBalanceResponse> GetBalanceAsync(DerivationStrategyBase userDerivationScheme, CancellationToken cancellation = default)
		{
			return GetBalanceAsync(TrackedSource.Create(userDerivationScheme), cancellation);
		}


		public GetBalanceResponse GetBalance(BitcoinAddress address, CancellationToken cancellation = default)
		{
			return GetBalanceAsync(address, cancellation).GetAwaiter().GetResult();
		}
		public Task<GetBalanceResponse> GetBalanceAsync(BitcoinAddress address, CancellationToken cancellation = default)
		{
			return GetBalanceAsync(TrackedSource.Create(address), cancellation);
		}
		public Task<GetBalanceResponse> GetBalanceAsync(TrackedSource trackedSource, CancellationToken cancellation = default)
		{
			return SendAsync<GetBalanceResponse>(HttpMethod.Get, null, $"{GetBasePath(trackedSource)}/balance", cancellation);
		}
		
		public async Task<BitcoinAddress[]> GetAddresses(TrackedSource trackedSource, CancellationToken cancellation = default)
		{
			var addresses =  await SendAsync<string[]>(HttpMethod.Get, null, $"{GetBasePath(trackedSource)}/addresses", cancellation);
			return addresses.Select(s => BitcoinAddress.Create(s, Network.NBitcoinNetwork)).ToArray();
		}
		public async Task<bool> IsTrackedAsync(TrackedSource trackedSource, CancellationToken cancellation = default)
		{
			
			var responseMessage = await  SendAsync(HttpMethod.Get, null, $"{GetBasePath(trackedSource)}", cancellation);
			switch (responseMessage.StatusCode)
			{
				case HttpStatusCode.OK:
					return true;
				case HttpStatusCode.NotFound:
					return false;
				default:
					await ParseResponse(responseMessage);
					return false;
			}
		}
		public Task CancelReservationAsync(DerivationStrategyBase strategy, KeyPath[] keyPaths, CancellationToken cancellation = default)
		{
			return SendAsync<string>(HttpMethod.Post, keyPaths, $"v1/cryptos/{CryptoCode}/derivations/{strategy}/addresses/cancelreservation", cancellation);
		}

		public StatusResult GetStatus(CancellationToken cancellation = default)
		{
			return GetStatusAsync(cancellation).GetAwaiter().GetResult();
		}
		public void Wipe(DerivationStrategyBase strategy, CancellationToken cancellation = default)
		{
			WipeAsync(strategy, cancellation).GetAwaiter().GetResult();
		}
		public Task WipeAsync(DerivationStrategyBase strategy, CancellationToken cancellation = default)
		{
			if (strategy is null)
				throw new ArgumentNullException(nameof(strategy));
			return SendAsync<bool>(HttpMethod.Post, null, $"v1/cryptos/{CryptoCode}/derivations/{strategy}/utxos/wipe", cancellation);
		}

		public Task<StatusResult> GetStatusAsync(CancellationToken cancellation = default)
		{
			return SendAsync<StatusResult>(HttpMethod.Get, null, $"v1/cryptos/{CryptoCode}/status", cancellation);
		}
		public GetTransactionsResponse GetTransactions(DerivationStrategyBase strategy, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellation = default)
		{
			return GetTransactionsAsync(strategy, from, to, cancellation).GetAwaiter().GetResult();
		}
		public GetTransactionsResponse GetTransactions(TrackedSource trackedSource, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellation = default)
		{
			return GetTransactionsAsync(trackedSource, from, to, cancellation).GetAwaiter().GetResult();
		}

		public Task<GetTransactionsResponse> GetTransactionsAsync(DerivationStrategyBase strategy, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellation = default)
		{
			return GetTransactionsAsync(TrackedSource.Create(strategy), from, to, cancellation);
		}
		public Task<GetTransactionsResponse> GetTransactionsAsync(TrackedSource trackedSource, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellation = default)
		{
			string fromV = string.Empty;
			string toV = string.Empty;
			if (from is DateTimeOffset f)
			{
				fromV = NBitcoin.Utils.DateTimeToUnixTime(f).ToString();
			}
			if (to is DateTimeOffset t)
			{
				toV = NBitcoin.Utils.DateTimeToUnixTime(t).ToString();
			}
			return SendAsync<GetTransactionsResponse>(HttpMethod.Get, null, $"{GetBasePath(trackedSource)}/transactions?from={fromV}&to={toV}", cancellation);
		}


		public TransactionInformation GetTransaction(TrackedSource trackedSource, uint256 txId, CancellationToken cancellation = default)
		{
			return this.GetTransactionAsync(trackedSource, txId, cancellation).GetAwaiter().GetResult();
		}
		public TransactionInformation GetTransaction(DerivationStrategyBase derivationStrategyBase, uint256 txId, CancellationToken cancellation = default)
		{
			return this.GetTransactionAsync(derivationStrategyBase, txId, cancellation).GetAwaiter().GetResult();
		}
		public Task<TransactionInformation> GetTransactionAsync(DerivationStrategyBase derivationStrategyBase, uint256 txId, CancellationToken cancellation = default)
		{
			if (derivationStrategyBase == null)
				throw new ArgumentNullException(nameof(derivationStrategyBase));
			return GetTransactionAsync(new DerivationSchemeTrackedSource(derivationStrategyBase), txId, cancellation);
		}

		public Task<TransactionInformation> GetTransactionAsync(TrackedSource trackedSource, uint256 txId, CancellationToken cancellation = default)
		{
			if (txId == null)
				throw new ArgumentNullException(nameof(txId));
			if (trackedSource == null)
				throw new ArgumentNullException(nameof(trackedSource));
			return SendAsync<TransactionInformation>(HttpMethod.Get, null, $"{GetBasePath(trackedSource)}/transactions/{txId}", cancellation);
		}

		public Task RescanAsync(RescanRequest rescanRequest, CancellationToken cancellation = default)
		{
			if (rescanRequest == null)
				throw new ArgumentNullException(nameof(rescanRequest));
			return SendAsync<byte[]>(HttpMethod.Post, rescanRequest, $"v1/cryptos/{CryptoCode}/rescan", cancellation);
		}

		public void Rescan(RescanRequest rescanRequest, CancellationToken cancellation = default)
		{
			RescanAsync(rescanRequest, cancellation).GetAwaiter().GetResult();
		}

		public KeyPathInformation GetUnused(DerivationStrategyBase strategy, DerivationFeature feature, int skip = 0, bool reserve = false, CancellationToken cancellation = default)
		{
			return GetUnusedAsync(strategy, feature, skip, reserve, cancellation).GetAwaiter().GetResult();
		}

		public async Task<KeyPathInformation> GetUnusedAsync(DerivationStrategyBase strategy, DerivationFeature feature, int skip = 0, bool reserve = false, CancellationToken cancellation = default)
		{
			try
			{
				return await GetAsync<KeyPathInformation>($"v1/cryptos/{CryptoCode}/derivations/{strategy}/addresses/unused?feature={feature}&skip={skip}&reserve={reserve}", cancellation).ConfigureAwait(false);
			}
			catch (NBXplorerException ex) when (ex.Error?.HttpCode == 404)
			{
				return null;
			}
		}

		public KeyPathInformation GetKeyInformation(DerivationStrategyBase strategy, Script script, CancellationToken cancellation = default)
		{
			return GetKeyInformationAsync(strategy, script, cancellation).GetAwaiter().GetResult();
		}

		public async Task<KeyPathInformation> GetKeyInformationAsync(DerivationStrategyBase strategy, Script script, CancellationToken cancellation = default)
		{
			return await GetKeyInformationAsync(new DerivationSchemeTrackedSource(strategy), script, cancellation).ConfigureAwait(false);
		}
		public async Task<KeyPathInformation> GetKeyInformationAsync(TrackedSource trackedSource, Script script, CancellationToken cancellation = default)
		{
			return await SendAsync<KeyPathInformation>(HttpMethod.Get, null, $"{GetBasePath(trackedSource)}/scripts/{script.ToHex()}", cancellation).ConfigureAwait(false);
		}
		public async Task<KeyPathInformation[]> GetKeyInformationsAsync(Script script, CancellationToken cancellation = default)
		{
			return await SendAsync<KeyPathInformation[]>(HttpMethod.Get, null, $"v1/cryptos/{CryptoCode}/scripts/{script.ToHex()}", cancellation).ConfigureAwait(false);
		}

		public KeyPathInformation[] GetKeyInformations(Script script, CancellationToken cancellation = default)
		{
			return GetKeyInformationsAsync(script, cancellation).GetAwaiter().GetResult();
		}

		public GetFeeRateResult GetFeeRate(int blockCount, CancellationToken cancellation = default)
		{
			return GetFeeRateAsync(blockCount, cancellation).GetAwaiter().GetResult();
		}

		public GetFeeRateResult GetFeeRate(int blockCount, FeeRate fallbackFeeRate, CancellationToken cancellation = default)
		{
			return GetFeeRateAsync(blockCount, fallbackFeeRate, cancellation).GetAwaiter().GetResult();
		}
		public async Task<GetFeeRateResult> GetFeeRateAsync(int blockCount, FeeRate fallbackFeeRate, CancellationToken cancellation = default)
		{
			try
			{
				return await GetAsync<GetFeeRateResult>($"v1/cryptos/{CryptoCode}/fees/{blockCount}", cancellation).ConfigureAwait(false);
			}
			catch (NBXplorerException ex) when (fallbackFeeRate != null && ex.Error.Code == "fee-estimation-unavailable")
			{
				return new GetFeeRateResult() { BlockCount = blockCount, FeeRate = fallbackFeeRate };
			}
		}
		public Task<GetFeeRateResult> GetFeeRateAsync(int blockCount, CancellationToken cancellation = default)
		{
			return GetAsync<GetFeeRateResult>($"v1/cryptos/{CryptoCode}/fees/{blockCount}", cancellation);
		}
		public CreatePSBTResponse CreatePSBT(DerivationStrategyBase derivationStrategy, CreatePSBTRequest request, CancellationToken cancellation = default)
		{
			return CreatePSBTAsync(derivationStrategy, request, cancellation).GetAwaiter().GetResult();
		}
		public Task<CreatePSBTResponse> CreatePSBTAsync(DerivationStrategyBase derivationStrategy, CreatePSBTRequest request, CancellationToken cancellation = default)
		{
			if (derivationStrategy == null)
				throw new ArgumentNullException(nameof(derivationStrategy));
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			return this.SendAsync<CreatePSBTResponse>(HttpMethod.Post, request, $"v1/cryptos/{CryptoCode}/derivations/{derivationStrategy}/psbt/create", cancellation);
		}

		public UpdatePSBTResponse UpdatePSBT(UpdatePSBTRequest request, CancellationToken cancellation = default)
		{
			return UpdatePSBTAsync(request, cancellation).GetAwaiter().GetResult();
		}
		public Task<UpdatePSBTResponse> UpdatePSBTAsync(UpdatePSBTRequest request, CancellationToken cancellation = default)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			return this.SendAsync<UpdatePSBTResponse>(HttpMethod.Post, request, $"v1/cryptos/{CryptoCode}/psbt/update", cancellation);
		}
		public BroadcastResult Broadcast(Transaction tx, CancellationToken cancellation = default)
		{
			return Broadcast(tx, false, cancellation);
		}
		public BroadcastResult Broadcast(Transaction tx, bool testMempoolAccept, CancellationToken cancellation = default)
		{
			return BroadcastAsync(tx, testMempoolAccept, cancellation).GetAwaiter().GetResult();
		}

		public Task<BroadcastResult> BroadcastAsync(Transaction tx, CancellationToken cancellation = default)
		{
			return BroadcastAsync(tx, false, cancellation);
		}

		public Task<BroadcastResult> BroadcastAsync(Transaction tx, bool testMempoolAccept, CancellationToken cancellation = default)
		{
			return SendAsync<BroadcastResult>(HttpMethod.Post, tx.ToBytes(), $"v1/cryptos/{CryptoCode}/transactions?testMempoolAccept={testMempoolAccept}", cancellation);
		}

		public TMetadata GetMetadata<TMetadata>(DerivationStrategyBase derivationScheme, string key, CancellationToken cancellationToken = default)
		{
			return GetMetadataAsync<TMetadata>(derivationScheme, key, cancellationToken).GetAwaiter().GetResult();
		}
		public Task<TMetadata> GetMetadataAsync<TMetadata>(DerivationStrategyBase derivationScheme, string key, CancellationToken cancellationToken = default)
		{
			if (derivationScheme == null)
				throw new ArgumentNullException(nameof(derivationScheme));
			if (key == null)
				throw new ArgumentNullException(nameof(key));
			return GetAsync<TMetadata>($"v1/cryptos/{CryptoCode}/derivations/{derivationScheme}/metadata/{key}", cancellationToken);
		}

		public void SetMetadata<TMetadata>(DerivationStrategyBase derivationScheme, string key, TMetadata value, CancellationToken cancellationToken = default)
		{
			SetMetadataAsync<TMetadata>(derivationScheme, key, value, cancellationToken).GetAwaiter().GetResult();
		}

		public Task SetMetadataAsync<TMetadata>(DerivationStrategyBase derivationScheme, string key, TMetadata value, CancellationToken cancellationToken = default)
		{
			return SendAsync<string>(HttpMethod.Post, value, $"v1/cryptos/{CryptoCode}/derivations/{derivationScheme}/metadata/{key}", cancellationToken);
		}

		public Task<GenerateWalletResponse> GenerateWalletAsync(GenerateWalletRequest request = null, CancellationToken cancellationToken = default)
		{
			request ??= new GenerateWalletRequest();
			return SendAsync<GenerateWalletResponse>(HttpMethod.Post, request, $"v1/cryptos/{CryptoCode}/derivations", cancellationToken);
		}

		public GenerateWalletResponse GenerateWallet(GenerateWalletRequest request = null, CancellationToken cancellationToken = default)
		{
			request ??= new GenerateWalletRequest();
			return GenerateWalletAsync(request, cancellationToken).GetAwaiter().GetResult();
		}

		public Task<GroupInformation> CreateGroupAsync(CancellationToken cancellationToken = default)
		{
			return SendAsync<GroupInformation>(HttpMethod.Post, null, $"v1/groups", cancellationToken);
		}
		public Task<GroupInformation> GetGroupAsync(string groupId, CancellationToken cancellationToken = default)
		{
			return SendAsync<GroupInformation>(HttpMethod.Get, null, $"v1/groups/{groupId}", cancellationToken);
		}
		public Task<GroupInformation> AddGroupChildrenAsync(string groupId, GroupChild[] children, CancellationToken cancellationToken = default)
		{
			return SendAsync<GroupInformation>(HttpMethod.Post, children, $"v1/groups/{groupId}/children", cancellationToken);
		}
		public Task<GroupInformation> RemoveGroupChildrenAsync(string groupId, GroupChild[] children, CancellationToken cancellationToken = default)
		{
			return SendAsync<GroupInformation>(HttpMethod.Delete, children, $"v1/groups/{groupId}/children", cancellationToken);
		}

		public Task AddGroupAddressAsync(string cryptoCode, string groupId, string[] addresses, CancellationToken cancellationToken = default)
		{
			return SendAsync<GroupInformation>(HttpMethod.Post, addresses, $"v1/cryptos/{cryptoCode}/groups/{groupId}/addresses", cancellationToken);
		}
		
		public async Task ImportUTXOs(string cryptoCode, ImportUTXORequest request, CancellationToken cancellation = default)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			if (cryptoCode == null)
				throw new ArgumentNullException(nameof(cryptoCode));
			await SendAsync(HttpMethod.Post, request, $"v1/cryptos/{cryptoCode}/rescan-utxos", cancellation);
		}

		private static readonly HttpClient SharedClient = new HttpClient();
		internal HttpClient Client = SharedClient;

		public void SetClient(HttpClient client)
		{
			Client = client;
		}

		private readonly NBXplorerNetwork _Network;
		public NBXplorerNetwork Network
		{
			get
			{
				return _Network;
			}
		}


		private readonly Uri _Address;
		public Uri Address
		{
			get
			{
				return _Address;
			}
		}

		public bool IncludeTransaction
		{
			get; set;
		} = true;
		public Serializer Serializer { get; private set; }

		internal IAuth Auth
		{
			get => _Auth;
			set
			{
				_Auth = value;
				ConstructRPCClient();
			}
		}

		static FormattableString EncodeUrlParameters(FormattableString url)
		{
			return FormattableStringFactory.Create(
					url.Format,
					url.GetArguments()
							.Select(a =>
							a is RawStr ? a :
							a is FormattableString o ? EncodeUrlParameters(o) :
							Uri.EscapeDataString(a?.ToString() ?? ""))
							.ToArray());
		}
		internal string GetFullUri(FormattableString relativePath)
		{
			var uri = Address.AbsoluteUri;
			if (!uri.EndsWith("/", StringComparison.Ordinal))
				uri += "/";
			uri += EncodeUrlParameters(relativePath).ToString();
			if (!IncludeTransaction)
			{
				if (uri.IndexOf('?') == -1)
					uri += $"?includeTransaction=false";
				else
					uri += $"&includeTransaction=false";
			}
			return uri;
		}
		private Task<T> GetAsync<T>(FormattableString relativePath, CancellationToken cancellation)
		{
			return SendAsync<T>(HttpMethod.Get, null, relativePath, cancellation);
		}
		internal async Task<T> SendAsync<T>(HttpMethod method, object body, FormattableString relativePath, CancellationToken cancellation)
		{
			HttpRequestMessage message = CreateMessage(method, body, relativePath);
			var result = await Client.SendAsync(message, cancellation).ConfigureAwait(false);
			if ((int)result.StatusCode == 404)
			{
				return default(T);
			}
			if (result.StatusCode == HttpStatusCode.GatewayTimeout || result.StatusCode == HttpStatusCode.RequestTimeout)
			{
				throw new HttpRequestException($"HTTP error {(int)result.StatusCode}", new TimeoutException());
			}
			if ((int)result.StatusCode == 401)
			{
				if (Auth.RefreshCache())
				{
					message = CreateMessage(method, body, relativePath);
					result = await Client.SendAsync(message, cancellation).ConfigureAwait(false);
				}
			}
			return await ParseResponse<T>(result).ConfigureAwait(false);
		}
		internal async Task<HttpResponseMessage> SendAsync(HttpMethod method, object body, FormattableString relativePath, CancellationToken cancellation)
		{
			var message = CreateMessage(method, body, relativePath);
			var result = await Client.SendAsync(message, cancellation).ConfigureAwait(false);

			if (result.StatusCode == HttpStatusCode.GatewayTimeout || result.StatusCode == HttpStatusCode.RequestTimeout)
			{
				throw new HttpRequestException($"HTTP error {(int)result.StatusCode}", new TimeoutException());
			}
			if ((int)result.StatusCode == 401)
			{
				if (Auth.RefreshCache())
				{
					message = CreateMessage(method, body, relativePath);
					result = await Client.SendAsync(message, cancellation).ConfigureAwait(false);
				}
			}
			return result;
		}

		internal HttpRequestMessage CreateMessage(HttpMethod method, object body, FormattableString relativePath)
		{
			var uri = GetFullUri(relativePath);
			var message = new HttpRequestMessage(method, uri);
			Auth.SetAuthorization(message);
			if (body != null)
			{
				if (body is byte[])
				{
					var content = new ByteArrayContent((byte[])body);
					content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
					message.Content = content;
				}
				else
					message.Content = new StringContent(Serializer.ToString(body), Encoding.UTF8, "application/json");
			}
			return message;
		}

		private async Task<T> ParseResponse<T>(HttpResponseMessage response)
		{
			using (response)
			{
				if (response.IsSuccessStatusCode)
					if (response.Content.Headers.ContentLength == 0)
						return default(T);
					else if (response.Content.Headers.ContentType.MediaType.Equals("application/json", StringComparison.Ordinal))
						return Serializer.ToObject<T>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
					else if (response.Content.Headers.ContentType.MediaType.Equals("application/octet-stream", StringComparison.Ordinal))
					{
						return (T)(object)await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
					}
				if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
					response.EnsureSuccessStatusCode();
				var error = Serializer.ToObject<NBXplorerError>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
				if (error == null)
					response.EnsureSuccessStatusCode();
				throw error.AsException();
			}
		}

		private async Task ParseResponse(HttpResponseMessage response)
		{
			using (response)
			{
				if (response.IsSuccessStatusCode)
					return;
				if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
					response.EnsureSuccessStatusCode();
				var error = Serializer.ToObject<NBXplorerError>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
				if (error == null)
					response.EnsureSuccessStatusCode();
				throw error.AsException();
			}
		}

		private FormattableString GetBasePath(TrackedSource trackedSource)
		{
			if (trackedSource is null)
				throw new ArgumentNullException(nameof(trackedSource));
			return trackedSource switch
			{
				DerivationSchemeTrackedSource dsts => $"v1/cryptos/{CryptoCode}/derivations/{dsts.DerivationStrategy}",
				AddressTrackedSource asts => $"v1/cryptos/{CryptoCode}/addresses/{asts.Address}",
				GroupTrackedSource wts => $"v1/cryptos/{CryptoCode}/groups/{wts.GroupId}",
				_ => $"v1/cryptos/{CryptoCode}/tracked-sources/{trackedSource}"
			};
		}
	}
}
