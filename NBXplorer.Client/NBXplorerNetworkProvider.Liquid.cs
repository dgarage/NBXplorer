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
			public override async Task<TransactionResult> GetTransactionAsync(uint256 txId,
				CancellationToken cancellation = default)
			{
				return await base.GetTransactionAsync<LiquidTransactionResult>(txId, cancellation);
			}
		}


		public class  LiquidTransactionResult : TransactionResult
		{
			private ElementsTransaction _transaction;
			public override Transaction Transaction
			{
				get => _transaction;
				set => _transaction = value as ElementsTransaction;
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
			
			public override BitcoinAddress CreateAddress(DerivationStrategyBase derivationStrategy, KeyPath keyPath, Script scriptPubKey)
			{
				if (derivationStrategy is IHasDerivationStrategyOptions strategyOptions &&
				    strategyOptions.DerivationStrategyOptions.AdditionalOptions.TryGetValue("unblinded",
					    out var unblinded) && (bool) unblinded)
				{
					return base.CreateAddress(derivationStrategy, keyPath, scriptPubKey);
				}
				
				var blindingKey = GenerateBlindingKey(derivationStrategy, keyPath);
				if (blindingKey == null)
				{
					return base.CreateAddress(derivationStrategy, keyPath, scriptPubKey);
				}
				var blindingPubKey = blindingKey.PubKey;
				return new BitcoinBlindedAddress(blindingPubKey, base.CreateAddress(derivationStrategy, keyPath, scriptPubKey));
			}

			public static Key GenerateBlindingKey(DerivationStrategyBase derivationStrategy, KeyPath keyPath)
			{
				if (derivationStrategy == null)
				{
					return null;
				}
				var blindingKey = new Key(derivationStrategy.GetChild(keyPath).GetChild(new KeyPath("0")).GetDerivation()
					.ScriptPubKey.Hash.ToBytes());
				return blindingKey;
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

		class LiquidDerivationStrategyFactory : DerivationStrategyFactory
		{
			public LiquidDerivationStrategyFactory(Network network) : base(network)
			{
			}

			public override DerivationStrategyBase Parse(string str)
			{
				var options = new Dictionary<string, object>();
				var unblinded = false;
				ReadBool(ref str, "unblinded", ref unblinded);
				options.Add("unblinded", unblinded);
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

			class LiquidDirectDerivationStrategy : DirectDerivationStrategy, IHasDerivationStrategyOptions
			{

				public LiquidDirectDerivationStrategy(DirectDerivationStrategy derivationStrategy,
					DerivationStrategyOptions options) : base(derivationStrategy
					.RootBase58)
				{
					DerivationStrategyOptions = options;
					Segwit = derivationStrategy.Segwit;
				}
				
				public override DerivationStrategyBase GetChild(KeyPath keyPath)
				{
					var result = base.GetChild(keyPath);
					switch (result)
					{
						case DirectDerivationStrategy directDerivationStrategy:
							return new LiquidDirectDerivationStrategy(directDerivationStrategy, DerivationStrategyOptions);
						case P2SHDerivationStrategy p2ShDerivationStrategy:
							return new LiquidP2SHDerivationStrategy(p2ShDerivationStrategy,DerivationStrategyOptions);
					}

					return result;

				}
				
				protected override string StringValue
				{
					get
					{
						var result = base.StringValue;
						
						if (DerivationStrategyOptions.AdditionalOptions.TryGetValue("unblinded", out var unblinded) && 
						    (bool)unblinded)
						{
							result += $"-[unblinded]";
						}
						return result;
					}
				}

				public DerivationStrategyOptions DerivationStrategyOptions { get; }
			}

			class LiquidP2SHDerivationStrategy : P2SHDerivationStrategy, IHasDerivationStrategyOptions
			{

				public LiquidP2SHDerivationStrategy(P2SHDerivationStrategy derivationStrategy,
					DerivationStrategyOptions options) : base(
					derivationStrategy.Inner, derivationStrategy.AddSuffix)
				{
					DerivationStrategyOptions = options;
				}

				public override DerivationStrategyBase GetChild(KeyPath keyPath)
				{
					return new LiquidP2SHDerivationStrategy((P2SHDerivationStrategy) base.GetChild(keyPath), DerivationStrategyOptions);
				}
				protected override string StringValue
				{

					get
					{
						var result = base.StringValue;
						
						if (DerivationStrategyOptions.AdditionalOptions.TryGetValue("unblinded", out var unblinded) && 
						    (bool)unblinded)
						{
							result += $"-[unblinded]";
						}
						return result;
					}
				}

				public DerivationStrategyOptions DerivationStrategyOptions { get; }
			}
		}
	}
}