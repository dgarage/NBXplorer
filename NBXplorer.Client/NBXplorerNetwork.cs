using NBitcoin;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NBXplorer
{
	public class NBXplorerNetwork
	{
		public NBXplorerNetwork(INetworkSet networkSet, NBitcoin.NetworkType networkType)
		{
			NBitcoinNetwork = networkSet.GetNetwork(networkType);
			CryptoCode = networkSet.CryptoCode;
			DefaultSettings = NBXplorerDefaultSettings.GetDefaultSettings(networkType);
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
				_Serializer = _Serializer ?? new Serializer(NBitcoinNetwork);
				return _Serializer;
			}
		}


		private JsonSerializerSettings _JsonSerializerSettings;
		public JsonSerializerSettings JsonSerializerSettings
		{
			get
			{
				if (_JsonSerializerSettings == null)
				{
					var json = new JsonSerializerSettings();
					Serializer.ConfigureSerializer(json);
					_JsonSerializerSettings = json;
				}
				return _JsonSerializerSettings;
			}
		}

		

		public TimeSpan ChainLoadingTimeout
		{
			get;
			set;
		} = TimeSpan.FromMinutes(15);

		/// <summary>
		/// Minimum blocks to keep if pruning is activated
		/// </summary>
		public int MinBlocksToKeep
		{
			get; set;
		} = 288;

		public override string ToString()
		{
			return CryptoCode.ToString();
		}
	}
}
