using NBitcoin;
using System;
using NBitcoin.Altcoins.Elements;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using NBXplorer.DerivationStrategy;

namespace NBXplorer
{
	public partial class NBXplorerNetworkProvider
	{
		public class LiquidNBXplorerNetwork : NBXplorerNetwork
		{
			internal LiquidNBXplorerNetwork(INetworkSet networkSet, ChainName networkType) : base(networkSet, networkType)
			{
			}

			internal override DerivationStrategyFactory CreateStrategyFactory()
			{
				var factory = base.CreateStrategyFactory();
				factory.AuthorizedOptions.Add("unblinded");
				factory.AuthorizedOptions.Add("slip77");
				return factory;
			}

			public override BitcoinAddress CreateAddress(DerivationStrategyBase derivationStrategy, KeyPath keyPath, Script scriptPubKey)
			{
				if (derivationStrategy.Unblinded())
				{
					return base.CreateAddress(derivationStrategy, keyPath, scriptPubKey);
				}
				var blindingPubKey = GenerateBlindingKey(derivationStrategy, keyPath, scriptPubKey, NBitcoinNetwork).PubKey;
				return new BitcoinBlindedAddress(blindingPubKey, base.CreateAddress(derivationStrategy, keyPath, scriptPubKey));
			}

			public static Key GenerateSlip77BlindingKeyFromMnemonic(Mnemonic mnemonic, Script script)
			{
				var seed = mnemonic.DeriveSeed();
				var slip21 = Slip21Node.FromSeed(seed);
				var slip77 = slip21.GetSlip77Node();
				return slip77.DeriveSlip77BlindingKey(script);
			}

			public static Key GenerateSlip77BlindingKeyFromMasterBlindingKey(Key masterBlindingKey, Script script)
			{
				return new Key(Hashes.HMACSHA256(masterBlindingKey.ToBytes(), script.ToBytes()));
			}

			public static Key GenerateBlindingKey(DerivationStrategyBase derivationStrategy, KeyPath keyPath, Script script, Network network)
			{
				if (derivationStrategy.Unblinded())
				{
					throw new InvalidOperationException("This derivation scheme is set to only track unblinded addresses");
				}

				if (derivationStrategy.Slip77(out var key))
				{
					if (HexEncoder.IsWellFormed(key))
					{
						return GenerateSlip77BlindingKeyFromMasterBlindingKey(new Key(Encoders.Hex.DecodeData(key)), script);
					}
					try
					{
						return GenerateSlip77BlindingKeyFromMasterBlindingKey(Key.Parse(key, network), script);
					}
					catch (Exception)
					{
						// ignored
					}
					try
					{
						var data = new Mnemonic(key);
						return GenerateSlip77BlindingKeyFromMnemonic(data, derivationStrategy.GetDerivation(keyPath).ScriptPubKey);
					}
					catch (Exception)
					{
						// ignored
					}

					throw new InvalidOperationException("The key provided for slip77 derivation was invalid.");
				}

				var blindingKey = new Key(derivationStrategy.GetChild(keyPath).GetChild(new KeyPath("0")).GetDerivation()
					.ScriptPubKey.WitHash.ToBytes());
				return blindingKey;
			}
		}
		private void InitLiquid(ChainName networkType)
		{
			Add(new LiquidNBXplorerNetwork(NBitcoin.Altcoins.Liquid.Instance, networkType)
			{
				MinRPCVersion = 150000,
				CoinType = networkType == ChainName.Mainnet ? new KeyPath("1776'") : new KeyPath("1'"),
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
			return derivationStrategyBase.AdditionalOptions.TryGetValue("unblinded", out _);
		}
		public static bool Slip77(this DerivationStrategyBase derivationStrategyBase ,out string key)
		{
			return derivationStrategyBase.AdditionalOptions.TryGetValue("slip77", out key);
		}
	}
}
