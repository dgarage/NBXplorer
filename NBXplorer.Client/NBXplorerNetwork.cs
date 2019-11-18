using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using System;

namespace NBXplorer
{
	public class NBXplorerNetwork
	{
		public NBXplorerNetwork(INetworkSet networkSet, NetworkType networkType,
			DerivationStrategyFactory derivationStrategyFactory = null)
		{
			NBitcoinNetwork = networkSet.GetNetwork(networkType);
			CryptoCode = networkSet.CryptoCode;
			DefaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(networkType);
			DerivationStrategyFactory = derivationStrategyFactory;
		}

		public Network NBitcoinNetwork
		{
			get;
			private set;
		}
		
		public int MinRPCVersion
		{
			get;
			internal set;
		}
		public string CryptoCode
		{
			get;
			private set;
		}
		public NBXplorerDefaultSettings DefaultSettings
		{
			get;
			private set;
		}
		public DerivationStrategy.DerivationStrategyFactory DerivationStrategyFactory
		{
			get;
			internal set;
		}

		public virtual BitcoinAddress CreateAddress(DerivationStrategyBase derivationStrategy, KeyPath keyPath, Script scriptPubKey)
		{
			return scriptPubKey.GetDestinationAddress(NBitcoinNetwork);
		}

		public bool SupportCookieAuthentication
		{
			get;
			internal set;
		} = true;


		private Serializer _Serializer;

		public Serializer Serializer
		{
			get
			{
				_Serializer = _Serializer ?? new Serializer(this);
				return _Serializer;
			}
		}

		public JsonSerializerSettings JsonSerializerSettings
		{
			get { return Serializer.Settings; }
		}

		public TimeSpan ChainLoadingTimeout { get; set; } = TimeSpan.FromMinutes(15);

		public TimeSpan ChainCacheLoadingTimeout { get; set; } = TimeSpan.FromSeconds(30);

		/// <summary>
		/// Minimum blocks to keep if pruning is activated
		/// </summary>
		public int MinBlocksToKeep
		{
			get; set;
		} = 288;
		public KeyPath CoinType { get; internal set; }

		public override string ToString()
		{
			return CryptoCode.ToString();
		}



		public virtual Task<Transaction> GetTransaction(RPCClient rpcClient, Transaction tx, KeyPathInformation keyInfo)
		{
			return Task.FromResult(tx);
			}
		public virtual ExplorerClient CreateExplorerClient(Uri uri)
		{
			return new ExplorerClient(this, uri);
		}

		public virtual KeyPathInformation GetKeyPathInformation(Derivation derivation, TrackedSource trackedSource,
			DerivationFeature derivationFeature, KeyPath keyPath)
		{
			return new KeyPathInformation()
			{
				ScriptPubKey = derivation.ScriptPubKey,
				Redeem = derivation.Redeem,
				TrackedSource = trackedSource,
				DerivationStrategy = trackedSource is DerivationSchemeTrackedSource derivationSchemeTrackedSource
					? derivationSchemeTrackedSource.DerivationStrategy
					: null,
				Feature = derivationFeature,
				KeyPath = keyPath
			};
		}

		public virtual KeyPathInformation GetKeyPathInformation(IDestination derivation)
		{
			return new KeyPathInformation()
			{
				ScriptPubKey = derivation.ScriptPubKey,
				TrackedSource = (TrackedSource) derivation
			};
		}
	}
}
