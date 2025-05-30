using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using System;

namespace NBXplorer
{
	public class NBXplorerNetwork
	{
		internal NBXplorerNetwork(INetworkSet networkSet, ChainName networkType)
		{
			NBitcoinNetwork = networkSet.GetNetwork(networkType);
			CryptoCode = networkSet.CryptoCode;
			DefaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(networkType);
		}
		public static uint256 UnknownTxId = uint256.Parse("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");
		public static string UnknownAssetId = uint256.Parse("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff").ToString();
		public static AssetMoney UnknownAssetMoney = new AssetMoney(uint256.Parse("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"), 1);
		public bool IsElement => NBitcoinNetwork.NetworkSet == NBitcoin.Altcoins.Liquid.Instance;
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

		internal virtual DerivationStrategyFactory CreateStrategyFactory()
		{
			return new DerivationStrategy.DerivationStrategyFactory(NBitcoinNetwork);
		}

		public DerivationStrategy.DerivationStrategyFactory DerivationStrategyFactory
		{
			get;
			internal set;
		}

		[Obsolete]
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
				_Serializer ??= new Serializer(this);
				return _Serializer;
			}
		}


		public JsonSerializerSettings JsonSerializerSettings
		{
			get
			{
				return Serializer.Settings;
			}
		}

		

		public TimeSpan ChainLoadingTimeout
		{
			get;
			set;
		} = TimeSpan.FromMinutes(15);

		public TimeSpan ChainCacheLoadingTimeout
		{
			get;
			set;
		} = TimeSpan.FromSeconds(30);

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
		
		public virtual ExplorerClient CreateExplorerClient(Uri uri)
		{
			return new ExplorerClient(this, uri);
		}
	}
}
