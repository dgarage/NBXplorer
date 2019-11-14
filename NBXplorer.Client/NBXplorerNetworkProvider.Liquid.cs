using NBitcoin;
using System;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin.Altcoins.Elements;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;

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
				var blindingKey = new Key(derivationStrategy.GetChild(keyPath).GetChild(new KeyPath("0")).GetDerivation()
					.ScriptPubKey.Hash.ToBytes());
				return blindingKey;
			}
		}
		private void InitLiquid(NetworkType networkType)
		{
			Add(new LiquidNBXplorerNetwork(NBitcoin.Altcoins.Liquid.Instance, networkType)
			{
				MinRPCVersion = 150000
			});
		}

		public NBXplorerNetwork GetLBTC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Liquid.Instance.CryptoCode);
		}
	}
}