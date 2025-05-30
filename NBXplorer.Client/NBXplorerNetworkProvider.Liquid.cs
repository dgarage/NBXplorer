using NBitcoin;
using System;
using NBitcoin.Altcoins.Elements;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using NBXplorer.DerivationStrategy;
#if !NO_RECORD
using NBitcoin.WalletPolicies;
#endif

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

			public BitcoinAddress BlindIfNeeded(DerivationStrategyBase derivationStrategy, BitcoinAddress address, KeyPath keyPath)
			{
				if (derivationStrategy.Unblinded() || address is BitcoinBlindedAddress)
					return address;
				var blindingPubKey = GenerateBlindingKey(derivationStrategy, keyPath, address.ScriptPubKey, NBitcoinNetwork).PubKey;
				return new BitcoinBlindedAddress(blindingPubKey, address);
			}

			[Obsolete]
			public override BitcoinAddress CreateAddress(DerivationStrategyBase derivationStrategy, KeyPath keyPath, Script scriptPubKey)
			{
				var addr = scriptPubKey.GetDestinationAddress(NBitcoinNetwork);
				return BlindIfNeeded(derivationStrategy, addr, keyPath);
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

			public static Key GenerateBlindingKey(DerivationStrategyBase derivationStrategy, KeyPath keyPath, Script scriptPubKey, Network network)
			{
				if (derivationStrategy.Unblinded())
				{
					throw new InvalidOperationException("This derivation scheme is set to only track unblinded addresses");
				}

				if (derivationStrategy.Slip77(out var key))
				{
					if (HexEncoder.IsWellFormed(key))
					{
						return GenerateSlip77BlindingKeyFromMasterBlindingKey(new Key(Encoders.Hex.DecodeData(key)), scriptPubKey);
					}
					try
					{
						return GenerateSlip77BlindingKeyFromMasterBlindingKey(Key.Parse(key, network), scriptPubKey);
					}
					catch (Exception)
					{
						// ignored
					}
					try
					{
						var data = new Mnemonic(key);
						return GenerateSlip77BlindingKeyFromMnemonic(data, scriptPubKey);
					}
					catch (Exception)
					{
						// ignored
					}

					throw new InvalidOperationException("The key provided for slip77 derivation was invalid.");
				}
				else if (derivationStrategy is StandardDerivationStrategyBase kpd && keyPath is not null)
				{
					var blindingKey = new Key(kpd.GetDerivation(keyPath.Derive(new KeyPath(0))).ScriptPubKey.WitHash.ToBytes());
					return blindingKey;
				}
				throw new InvalidOperationException("-[blinded] doesn't work on miniscript derivation strategies, use [slip77=key] instead");
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
