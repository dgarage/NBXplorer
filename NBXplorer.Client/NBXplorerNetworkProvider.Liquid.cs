using NBitcoin;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin.Altcoins.Elements;
using NBitcoin.JsonConverters;
using NBitcoin.RPC;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;

namespace NBXplorer
{
	public partial class NBXplorerNetworkProvider
	{

		public class LiquidExplorerClient : ExplorerClient
		{
			public LiquidExplorerClient(NBXplorerNetwork network, Uri serverAddress = null) : base(network, serverAddress)
			{
			}

			public override KeyPathInformation GetKeyInformation(DerivationStrategyBase strategy, Script script,
				CancellationToken cancellation = default)
			{
				return GetKeyInformation<LiquidKeyPathInformation>(strategy, script, cancellation);
			}

			public override async Task<KeyPathInformation> GetKeyInformationAsync(DerivationStrategyBase strategy, Script script, CancellationToken cancellation = default)
			{
				return await GetKeyInformationAsync<LiquidKeyPathInformation>(strategy, script, cancellation);
			}

			public override KeyPathInformation GetUnused(DerivationStrategyBase strategy, DerivationFeature feature, int skip = 0,
				bool reserve = false, CancellationToken cancellation = default)
			{
				return GetUnused<LiquidKeyPathInformation>(strategy, feature, skip, reserve, cancellation);
			}

			public override async Task<KeyPathInformation> GetUnusedAsync(DerivationStrategyBase strategy, DerivationFeature feature, int skip = 0, bool reserve = false,
				CancellationToken cancellation = default)
			{
				return await GetUnusedAsync<LiquidKeyPathInformation>(strategy, feature, skip, reserve, cancellation);
			}
		}
		
		public class LiquidNBXplorerNetwork : NBXplorerNetwork
		{
			public LiquidNBXplorerNetwork(INetworkSet networkSet, NetworkType networkType, DerivationStrategyFactory derivationStrategyFactory = null) : base(networkSet, networkType, derivationStrategyFactory)
			{
			}

			public override ExplorerClient CreateExplorerClient(Uri uri)
			{
				return new LiquidExplorerClient(this, uri);
			}
		}
		private void InitLiquid(NetworkType networkType)
		{
			Add(new LiquidNBXplorerNetwork(NBitcoin.Altcoins.Liquid.Instance, networkType,
				new LiquidDerivationStrategyFactory(NBitcoin.Altcoins.Liquid.Instance.GetNetwork(networkType)))
			{
				MinRPCVersion = 150000
			});
		}

		public NBXplorerNetwork GetLBTC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Liquid.Instance.CryptoCode);
		}

		public class LiquidKeyPathInformation:KeyPathInformation
		{
			public LiquidKeyPathInformation()
			{
				
			}

			public LiquidKeyPathInformation(KeyPathInformation keyPathInformation, Key blindingKey)
			{
				Address = keyPathInformation.Address;
				Feature = keyPathInformation.Feature;
				Redeem = keyPathInformation.Redeem;
				BlindingKey = blindingKey;
				DerivationStrategy = keyPathInformation.DerivationStrategy;
				KeyPath = keyPathInformation.KeyPath;
				TrackedSource = keyPathInformation.TrackedSource;
				ScriptPubKey = keyPathInformation.ScriptPubKey;
			}
			[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
			[JsonConverter(typeof(KeyJsonConverter))]
			public Key BlindingKey { get; set; }
			
			public override KeyPathInformation AddAddress(Network network, out BitcoinAddress address)
			{
				address = null;
				base.AddAddress(network, out var baseAddress);
				address = BlindingKey != null ? new BitcoinBlindedAddress(BlindingKey.PubKey, baseAddress) : baseAddress;
				Address = address.ToString();
				return this;
			}
		}
		
		public class LiquidDerivation : Derivation
		{
			public LiquidDerivation(Derivation derivation, Key blindingKey)
			{
				BlindingKey = blindingKey;
				ScriptPubKey = derivation.ScriptPubKey;
				Redeem = derivation.Redeem;
			}
			public Key BlindingKey { get; set; }

			public override BitcoinAddress GetAddress(Network network)
			{
				return new BitcoinBlindedAddress(BlindingKey.PubKey, base.GetAddress(network));
			}
		}

		class LiquidDerivationStrategyFactory : DerivationStrategyFactory
		{
			public LiquidDerivationStrategyFactory(Network network) : base(network)
			{
			}

			public override DerivationStrategyBase Parse(string str)
			{
				string blindKey =null;
				var options = new Dictionary<string, object>();
				if (ReadString(ref str, "blindingkey", ref blindKey))
				{
					options.Add("blindingkey", blindKey);
				}
				var strategy = ParseCore(str, options);
				return strategy;
			}
			

			public override DerivationStrategyBase CreateDirectDerivationStrategy(BitcoinExtPubKey publicKey,
				DerivationStrategyOptions options = null)
			{
				var result = base.CreateDirectDerivationStrategy(publicKey, options);
				switch (result)
				{
					case DirectDerivationStrategy directDerivationStrategy:
						return new LiquidDirectDerivationStrategy(directDerivationStrategy, options);
					case P2SHDerivationStrategy p2ShDerivationStrategy:
						return new LiquidP2SHDerivationStrategy(p2ShDerivationStrategy, options);
				}

				return result;
			}

			class LiquidDirectDerivationStrategy : DirectDerivationStrategy
			{
				private readonly DerivationStrategyOptions _options;

				public LiquidDirectDerivationStrategy(DirectDerivationStrategy derivationStrategy,
					DerivationStrategyOptions options) : base(derivationStrategy
					.RootBase58)
				{
					_options = options;
					Segwit = derivationStrategy.Segwit;
				}

				
				public override Derivation GetDerivation()
				{
					Key blindingKey = null;
					if (_options.AdditionalOptions.TryGetValue("blindingkey", out var blindkeyhex) )
					{
						blindingKey = new Key(NBitcoin.DataEncoders.Encoders.Hex.DecodeData(blindkeyhex.ToString()));
					}

					if (blindingKey == null)
					{
						return base.GetDerivation();
					}
					return new LiquidDerivation(base.GetDerivation(), blindingKey);
				}
				
				public override DerivationStrategyBase GetChild(KeyPath keyPath)
				{
					var result = base.GetChild(keyPath);
					switch (result)
					{
						case DirectDerivationStrategy directDerivationStrategy:
							return new LiquidDirectDerivationStrategy(directDerivationStrategy, _options);
						case P2SHDerivationStrategy p2ShDerivationStrategy:
							return new LiquidP2SHDerivationStrategy(p2ShDerivationStrategy,_options);
					}

					return result;

				}
				
				protected override string StringValue
				{
					get
					{
						var result = base.StringValue;
						
						if (_options.AdditionalOptions.TryGetValue("blindingkey", out var blindkeyhex) && 
						    !string.IsNullOrEmpty(blindkeyhex?.ToString()))
						{
							result += $"-[blindingkey={blindkeyhex}]";
						}
						return result;
					}
				}
			}

			class LiquidP2SHDerivationStrategy : P2SHDerivationStrategy
			{
				private readonly DerivationStrategyOptions _options;

				public LiquidP2SHDerivationStrategy(P2SHDerivationStrategy derivationStrategy,
					DerivationStrategyOptions options) : base(
					derivationStrategy.Inner, derivationStrategy.AddSuffix)
				{
					_options = options;
				}

				public override DerivationStrategyBase GetChild(KeyPath keyPath)
				{
					return new LiquidP2SHDerivationStrategy((P2SHDerivationStrategy) base.GetChild(keyPath), _options);
				}
				
				public override Derivation GetDerivation()
				{
					
					Key blindingKey = null;
					
					if (_options.AdditionalOptions.TryGetValue("blindingkey", out var blindkeyhex) )
					{
						blindingKey = new Key(NBitcoin.DataEncoders.Encoders.Hex.DecodeData(blindkeyhex.ToString()));
					}

					var result = base.GetDerivation();
					if (blindingKey == null)
					{
						return result;
					}
					if (Inner is DirectDerivationStrategy _)
					{
						return new LiquidDerivation(result, blindingKey);
					}

					return result;
				}

				protected override string StringValue
				{

					get
					{
						var result = base.StringValue;
						
						if (AddSuffix && _options.AdditionalOptions.TryGetValue("blindingkey", out var blindkeyhex)  && 
						    !string.IsNullOrEmpty(blindkeyhex?.ToString()))
						{
							result += $"-[blindingkey={blindkeyhex}]";
						}

						return result;
					}
				}
			}

			
		}
	}
}