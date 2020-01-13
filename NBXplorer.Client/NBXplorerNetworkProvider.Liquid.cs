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
		public class LiquidNBXplorerNetwork : NBXplorerNetwork
		{
			internal LiquidNBXplorerNetwork(INetworkSet networkSet, NetworkType networkType) : base(networkSet, networkType)
			{
			}

			internal override DerivationStrategyFactory CreateStrategyFactory()
			{
				var factory = base.CreateStrategyFactory();
				factory.AuthorizedOptions.Add("unblinded");
				return factory;
			}

			public override BitcoinAddress CreateAddress(DerivationStrategyBase derivationStrategy, KeyPath keyPath, Script scriptPubKey)
			{
				if (derivationStrategy.Unblinded())
				{
					return base.CreateAddress(derivationStrategy, keyPath, scriptPubKey);
				}
				var blindingPubKey = GenerateBlindingKey(derivationStrategy, keyPath).PubKey;
				return new BitcoinBlindedAddress(blindingPubKey, base.CreateAddress(derivationStrategy, keyPath, scriptPubKey));
			}

			public static Key GenerateBlindingKey(DerivationStrategyBase derivationStrategy, KeyPath keyPath)
			{
				if (derivationStrategy.Unblinded())
				{
					throw new InvalidOperationException("This derivation scheme is set to only track unblinded addresses");
				}
				var blindingKey = new Key(derivationStrategy.GetChild(keyPath).GetChild(new KeyPath("0")).GetDerivation()
					.ScriptPubKey.WitHash.ToBytes());
				return blindingKey;
			}
		}
		private void InitLiquid(NetworkType networkType)
		{
			Add(new LiquidNBXplorerNetwork(NBitcoin.Altcoins.Liquid.Instance, networkType)
			{
				MinRPCVersion = 150000,
				CoinType = networkType == NetworkType.Mainnet ? new KeyPath("1776'") : new KeyPath("1'"),
			});
		}

		public NBXplorerNetwork GetLBTC()
		{
			return GetFromCryptoCode(NBitcoin.Altcoins.Liquid.Instance.CryptoCode);
		}
	}
	
	public static class LiquidDerivationStrategyOptionsExtensions
	{
		public static bool Unblinded(this DerivationStrategyBase derivationStrategyBase)
		{
			return derivationStrategyBase.AdditionalOptions.TryGetValue("unblinded", out var unblinded) is true && unblinded;
		}
	}
}
