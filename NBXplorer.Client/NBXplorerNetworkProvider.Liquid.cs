using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NBitcoin.Altcoins.Elements;
using NBitcoin.RPC;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;

namespace NBXplorer
{
	public partial class NBXplorerNetworkProvider
	{
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

		class LiquidNBXplorerNetwork : NBXplorerNetwork
		{
			public LiquidNBXplorerNetwork(INetworkSet networkSet, NetworkType networkType,
				DerivationStrategyFactory derivationStrategyFactory = null) : base(networkSet, networkType,
				derivationStrategyFactory)
			{
			}

			public override async Task<Transaction> GetTransaction(RPCClient rpcClient, Transaction tx,
				KeyPathInformation keyInfo)
			{
				if (keyInfo is LiquidKeyPathInformation liquidKeyPathInformation &&
				    liquidKeyPathInformation.BlindingKey != null && tx is ElementsTransaction elementsTransaction)
				{
					return await rpcClient.UnblindTransaction(new List<UnblindTransactionBlindingAddressKey>()
						{
							new UnblindTransactionBlindingAddressKey()
							{
								Address = new BitcoinBlindedAddress(liquidKeyPathInformation.Address, NBitcoinNetwork),
								BlindingKey = liquidKeyPathInformation.BlindingKey
							}
						},
						elementsTransaction, NBitcoinNetwork);
				}

				return await base.GetTransaction(rpcClient, tx, keyInfo);
			}

			public override KeyPathInformation GetKeyPathInformation(Derivation derivation, TrackedSource trackedSource,
				DerivationFeature derivationFeature, KeyPath keyPath)
			{
				var result =  base.GetKeyPathInformation(derivation, trackedSource, derivationFeature, keyPath);
				return new LiquidKeyPathInformation(result,
					derivation is LiquidDerivation liquidDerivation ? liquidDerivation.BlindingKey : null);
			}

			public override KeyPathInformation GetKeyPathInformation(IDestination derivation)
			{
				if (derivation is BitcoinBlindedAddress bitcoinBlindedAddress)
				{
					throw new NotSupportedException("Individual blinded address tracking is not currently supported");
				}
				return base.GetKeyPathInformation(derivation);
			}
		}

		class LiquidKeyPathInformation:KeyPathInformation
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
			public Key BlindingKey { get; set; }
			
			public override KeyPathInformation AddAddress(Network network)
			{
				if(Address == null)
				{
					var address = ScriptPubKey.GetDestinationAddress(network);
					if (BlindingKey != null)
					{
						address = new BitcoinBlindedAddress(BlindingKey.PubKey, address);
					}

					Address = address.ToString();
				}
				return this;
			}
		}
		
		class LiquidDerivation : Derivation
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
				var unblinded = false;
				string blindKey =null;
				ReadBool(ref str, "unblinded", ref unblinded);
				ReadString(ref str, "blindingkey", ref blindKey);
				var strategy = ParseCore(str, new Dictionary<string, object>()
				{
					{"unblinded", unblinded},
					{"blindingkey", blindKey}
				});
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
					bool unblinded = true;
					Key blindingKey = null;
					if (_options.AdditionalOptions.TryGetValue("unblinded", out var unblindedflag) &&  (bool) unblindedflag)
					{
						unblinded = true;
					}
					if (_options.AdditionalOptions.TryGetValue("blindingkey", out var blindkeyhex) )
					{
						blindingKey = new Key(NBitcoin.DataEncoders.Encoders.Hex.DecodeData(blindkeyhex.ToString()));
					}

					if (unblinded || blindingKey == null)
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
					
					var unblinded = true;
					Key blindingKey = null;
					if (!_options.AdditionalOptions.TryGetValue("unblinded", out var unblindedflag) ||  !(bool) unblindedflag)
					{
						unblinded = false;
					}
					if (_options.AdditionalOptions.TryGetValue("blindingkey", out var blindkeyhex) )
					{
						blindingKey = new Key(NBitcoin.DataEncoders.Encoders.Hex.DecodeData(blindkeyhex.ToString()));
					}

					var result = base.GetDerivation();
					if (unblinded || blindingKey == null)
					{
						return result;
					}
					if (Inner is DirectDerivationStrategy _)
					{
						return new LiquidDerivation(result, blindingKey);
					}

					return result;
				}
			}

			
		}
	}
}