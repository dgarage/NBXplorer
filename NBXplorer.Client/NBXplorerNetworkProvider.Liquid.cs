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
				if (keyInfo is LiquidKeyPathInformation liquidKeyPathInformation && liquidKeyPathInformation.BlindingKey != null && tx is ElementsTransaction elementsTransaction)
				{
					return await rpcClient.UnblindTransaction(
						new BitcoinBlindedAddress(liquidKeyPathInformation.BlindingKey,
							BitcoinAddress.Create(keyInfo.Address, NBitcoinNetwork)),
						elementsTransaction);
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
				var result = base.GetKeyPathInformation(derivation);
				
				return new LiquidKeyPathInformation(result,
					derivation is BitcoinBlindedAddress bitcoinBlindedAddress ? bitcoinBlindedAddress.BlindingKey : null);
			}
		}

		class LiquidKeyPathInformation:KeyPathInformation
		{
			public LiquidKeyPathInformation()
			{
				
			}

			public LiquidKeyPathInformation(KeyPathInformation keyPathInformation, PubKey blindingKey)
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
			public PubKey BlindingKey { get; set; }
			
			public override KeyPathInformation AddAddress(Network network)
			{
				if(Address == null)
				{
					var address = ScriptPubKey.GetDestinationAddress(network);
					if (BlindingKey != null)
					{
						address = new BitcoinBlindedAddress(BlindingKey, address);
					}

					Address = address.ToString();
				}
				return this;
			}
		}
		
		class LiquidDerivation : Derivation
		{
			public LiquidDerivation(Derivation derivation, PubKey blindingKey)
			{
				BlindingKey = blindingKey;
				ScriptPubKey = derivation.ScriptPubKey;
				Redeem = derivation.Redeem;
			}
			public PubKey BlindingKey { get; set; }
		}

		class LiquidDerivationStrategyFactory : DerivationStrategyFactory
		{
			public LiquidDerivationStrategyFactory(Network network) : base(network)
			{
			}

			public override DerivationStrategyBase Parse(string str)
			{
				var unblinded = false;
				ReadBool(ref str, "unblinded", ref unblinded);
				var strategy = ParseCore(str, new Dictionary<string, object>()
				{
					{"unblinded", unblinded}
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
						return new LiquidDirectDerivationStrategy(directDerivationStrategy);
					case P2SHDerivationStrategy p2ShDerivationStrategy:
						return new LiquidP2SHDerivationStrategy(p2ShDerivationStrategy);
				}

				return result;
			}

			class LiquidDirectDerivationStrategy : DirectDerivationStrategy
			{
				private readonly DerivationStrategyOptions _options;

				public LiquidDirectDerivationStrategy(DirectDerivationStrategy derivationStrategy,
					DerivationStrategyOptions options = null) : base(derivationStrategy
					.RootBase58)
				{
					_options = options;
					Segwit = derivationStrategy.Segwit;
				}

				public override Derivation GetDerivation()
				{
					if (_options.AdditionalOptions.TryGetValue("unblinded", out var unblinded) &&
					    unblinded == (object) true)
					{
						return base.GetDerivation();
					}

					return new LiquidDerivation(base.GetDerivation(),Root.PubKey);
				}
			}

			class LiquidP2SHDerivationStrategy : P2SHDerivationStrategy
			{
				private readonly DerivationStrategyOptions _options;

				public LiquidP2SHDerivationStrategy(P2SHDerivationStrategy derivationStrategy,
					DerivationStrategyOptions options = null) : base(
					derivationStrategy.Inner, derivationStrategy.AddSuffix)
				{
					_options = options;
				}

				public override Derivation GetDerivation()
				{
					if (_options.AdditionalOptions.TryGetValue("unblinded", out var unblinded) &&
					    unblinded == (object) true)
					{
						return base.GetDerivation();
					}

					var result = base.GetDerivation();
					if (Inner is DirectDerivationStrategy directDerivationStrategy)
					{
						return new LiquidDerivation(result, directDerivationStrategy.Root.PubKey);
					}

					return result;
				}
			}

			
		}
	}
}