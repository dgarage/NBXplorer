using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Protocol;
using NBitcoin.RPC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NBXplorer.Altcoins.Feathercoin
{
	public class Networks
	{
		//Format visual studio
		//{({.*?}), (.*?)}
		//Tuple.Create(new byte[]$1, $2)
		static Tuple<byte[], int>[] pnSeed6_main = {
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xc6,0x0f,0x7f,0xf2}, 11533),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x49,0xa2,0x04,0xe8}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x49,0x88,0xa4,0xe1}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x46,0x47,0xf3,0xf9}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x43,0x80,0x24,0x5a}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xa2,0xef,0x65,0xf6}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x43,0xbd,0x0a,0x1a}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xb9,0x89,0x61,0x18}, 40004),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x9b,0x5e,0xbd,0x69}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x3e,0xd2,0x8b,0x29}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x58,0xde,0x96,0x1e}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x18,0x40,0x55,0x7f}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x7c,0xa9,0x58,0x16}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x4c,0xb8,0x1f,0x4f}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x5b,0x86,0xd9,0x43}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x65,0x64,0xae,0x8a}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x45,0x76,0x9b,0xac}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xbe,0x99,0x52,0xbf}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x45,0x87,0xd0,0xfb}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x4e,0x81,0xf1,0x91}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x18,0x77,0xbe,0xf6}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x5e,0xe4,0xff,0x84}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x68,0x81,0x39,0x71}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x55,0x65,0xd5,0x0c}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x4a,0xc0,0x8f,0x86}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x68,0xec,0xaa,0x39}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x43,0xf6,0x70,0x9b}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x49,0x2b,0xc0,0xa0}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x18,0x04,0x9f,0x55}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xbc,0x00,0x4b,0x92}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xbc,0x28,0x83,0x2b}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x18,0xb3,0x24,0x40}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x6c,0x33,0x98,0x1d}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x2d,0x4c,0x83,0x34}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x45,0xa2,0x43,0x03}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xb2,0xa9,0xa6,0x56}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x52,0x24,0x67,0x89}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xac,0x68,0x16,0x98}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x6d,0xa7,0xe3,0xf8}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x51,0xe7,0xf3,0xd3}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x8b,0xa2,0xd8,0x43}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x94,0x4b,0xb3,0x60}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x18,0xea,0x44,0x91}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x68,0xe1,0xdc,0x13}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x6b,0x96,0x34,0x7a}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x05,0x09,0x8e,0x36}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x5c,0x6c,0xc3,0x2d}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xcb,0x07,0x50,0x80}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x68,0xc7,0xdc,0x42}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xcf,0x86,0x2c,0x1f}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xb0,0xf0,0xdb,0x04}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x68,0xe1,0xd9,0x0c}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x49,0xf7,0xbd,0xef}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xbc,0x71,0x7a,0x32}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x51,0x62,0x48,0xf6}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x52,0x2e,0x7f,0x1c}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xd9,0x65,0x36,0x53}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x50,0x65,0xe3,0x67}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x50,0x65,0xe3,0x67}, 11333),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xc6,0x1b,0xe8,0x1a}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x18,0x4d,0x97,0x37}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x4d,0xf9,0xd5,0x19}, 5802),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x5c,0x61,0x4f,0x8c}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xb9,0x19,0x3c,0xc7}, 9329),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x48,0xb4,0x98,0x2e}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x46,0x48,0xc6,0x30}, 9338),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xb0,0x09,0x71,0x4b}, 9017),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x5b,0x79,0x85,0x65}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0xae,0x34,0x2a,0xb0}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x55,0x00,0x6c,0xe4}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x4d,0x2d,0x35,0x3f}, 9336),
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x4e,0x39,0xce,0x15}, 9336),
};
		static Tuple<byte[], int>[] pnSeed6_test = {
	Tuple.Create(new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xff,0xff,0x2e,0x04,0x00,0x65}, 19336),
};

		[Obsolete("Use EnsureRegistered instead")]
		public static void Register()
		{
			EnsureRegistered();
		}
		public static void EnsureRegistered()
		{
			if(_LazyRegistered.IsValueCreated)
				return;
			// This will cause RegisterLazy to evaluate
			new Lazy<object>[] { _LazyRegistered }.Select(o => o.Value != null).ToList();
		}
		static Lazy<object> _LazyRegistered = new Lazy<object>(RegisterLazy, false);

		private static object RegisterLazy()
		{
			var port = 9336;
			NetworkBuilder builder = new NetworkBuilder();
			_Mainnet = builder.SetConsensus(new Consensus()
			{
				SubsidyHalvingInterval = 2100000,
				MajorityEnforceBlockUpgrade = 750,
				MajorityRejectBlockOutdated = 950,
				MajorityWindow = 1000,
				BIP34Hash = new uint256("966e30dd04d09232f6f690a04664cd3258abe43eeda2f2291d93706aa494aa54"),
				PowLimit = new Target(new uint256("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")),
				PowTargetTimespan = TimeSpan.FromSeconds(60),
				PowTargetSpacing = TimeSpan.FromSeconds(60),
				PowAllowMinDifficultyBlocks = false,
				PowNoRetargeting = false,
				RuleChangeActivationThreshold = 15120,
				MinerConfirmationWindow = 20160,
				CoinbaseMaturity = 100,
				HashGenesisBlock = new uint256("12a765e31ffd4059bada1e25190f6e98c99d9714d334efa41a195a7e7e04bfe2"),
				GetPoWHash = GetPoWHash,
				LitecoinWorkCalculation = true
			})
			.SetBase58Bytes(Base58Type.PUBKEY_ADDRESS, new byte[] { 14 })
			.SetBase58Bytes(Base58Type.SCRIPT_ADDRESS, new byte[] { 5 })
			.SetBase58Bytes(Base58Type.SECRET_KEY, new byte[] { 142 })
			.SetBase58Bytes(Base58Type.EXT_PUBLIC_KEY, new byte[] { 0x04, 0x88, 0xBC, 0x26 })
			.SetBase58Bytes(Base58Type.EXT_SECRET_KEY, new byte[] { 0x04, 0x88, 0xDA, 0xEE })
			.SetBech32(Bech32Type.WITNESS_PUBKEY_ADDRESS, Encoders.Bech32("fc"))
			.SetBech32(Bech32Type.WITNESS_SCRIPT_ADDRESS, Encoders.Bech32("fc"))
			.SetMagic(0xFBA4C795)
			.SetPort(port)
			.SetRPCPort(9337)
			.SetName("ftc-main")
			.AddAlias("ftc-mainnet")
			.AddAlias("feathercoin-mainnet")
			.AddAlias("feathercoin-main")
			.AddDNSSeeds(new[]
			{
				new DNSSeedData("feathercoin.com", "dnsseed.feathercoin.com"),
				new DNSSeedData("feathercoin.com", "explorer2.feathercoin.com"),
				new DNSSeedData("ftc-c.com", "block.ftc-c.com"),
				new DNSSeedData("feathercoin.ch", "dnsseed-static.feathercoin.ch"),
			})
			.AddSeeds(ToSeed(pnSeed6_main))
			.SetGenesis(new Block(Encoders.Hex.DecodeData("010000000000000000000000000000000000000000000000000000000000000000000000d9ced4ed1130f7b7faad9be25323ffafa33232a17c3edf6cfd97bee6bafbdd97b9aa8e4ef0ff0f1ecd513f7c0101000000010000000000000000000000000000000000000000000000000000000000000000ffffffff4804ffff001d0104404e592054696d65732030352f4f63742f32303131205374657665204a6f62732c204170706c65e280997320566973696f6e6172792c2044696573206174203536ffffffff0100f2052a010000004341040184710fa689ad5023690c80f3a49c8f13f8d45b8c857fbcbc8bc4a8e4d3eb4b10f4d4604fa08dce601aaf0f470216fe1b51850b4acf21b179c45070ac7b03a9ac00000000")))
			.BuildAndRegister();

			builder = new NetworkBuilder();
			port = 19336;
			_Testnet = builder.SetConsensus(new Consensus()
			{
				SubsidyHalvingInterval = 2100000,
				MajorityEnforceBlockUpgrade = 51,
				MajorityRejectBlockOutdated = 75,
				MajorityWindow = 600,
				PowLimit = new Target(new uint256("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")),
				PowTargetTimespan = TimeSpan.FromSeconds(60),
				PowTargetSpacing = TimeSpan.FromSeconds(60),
				PowAllowMinDifficultyBlocks = true,
				PowNoRetargeting = false,
				RuleChangeActivationThreshold = 375,
				MinerConfirmationWindow = 500,
				CoinbaseMaturity = 100,
				HashGenesisBlock = new uint256("4966625a4b2851d9fdee139e56211a0d88575f59ed816ff5e6a63deb4e3e29a0"),
				GetPoWHash = GetPoWHash,
				LitecoinWorkCalculation = true
			})
			.SetBase58Bytes(Base58Type.PUBKEY_ADDRESS, new byte[] { 111 })
			.SetBase58Bytes(Base58Type.SCRIPT_ADDRESS, new byte[] { 196 })
			.SetBase58Bytes(Base58Type.SECRET_KEY, new byte[] { 239 })
			.SetBase58Bytes(Base58Type.EXT_PUBLIC_KEY, new byte[] { 0x04, 0x35, 0x87, 0xCF })
			.SetBase58Bytes(Base58Type.EXT_SECRET_KEY, new byte[] { 0x04, 0x35, 0x83, 0x94 })
			.SetBech32(Bech32Type.WITNESS_PUBKEY_ADDRESS, Encoders.Bech32("tfc"))
			.SetBech32(Bech32Type.WITNESS_SCRIPT_ADDRESS, Encoders.Bech32("tfc"))
			.SetMagic(0xf1c8d2fd)
			.SetPort(port)
			.SetRPCPort(19337)
			.SetName("ftc-test")
			.AddAlias("ftc-testnet")
			.AddAlias("feathercoin-test")
			.AddAlias("feathercoin-testnet")
			.AddDNSSeeds(new[]
			{
				new DNSSeedData("testnet-explorer2.feathercoin.com","feathercoin.com"),
				new DNSSeedData("testnet-dnsseed.feathercoin.com","feathercoin.com"),
			})
			.AddSeeds(ToSeed(pnSeed6_test))
			.SetGenesis(new Block(Encoders.Hex.DecodeData("010000000000000000000000000000000000000000000000000000000000000000000000d9ced4ed1130f7b7faad9be25323ffafa33232a17c3edf6cfd97bee6bafbdd97f60ba158f0ff0f1ee17904000101000000010000000000000000000000000000000000000000000000000000000000000000ffffffff4804ffff001d0104404e592054696d65732030352f4f63742f32303131205374657665204a6f62732c204170706c65e280997320566973696f6e6172792c2044696573206174203536ffffffff0100f2052a010000004341040184710fa689ad5023690c80f3a49c8f13f8d45b8c857fbcbc8bc4a8e4d3eb4b10f4d4604fa08dce601aaf0f470216fe1b51850b4acf21b179c45070ac7b03a9ac00000000")))
			.BuildAndRegister();

			builder = new NetworkBuilder();
			port = 18446;
			_Regtest = builder.SetConsensus(new Consensus()
			{
				SubsidyHalvingInterval = 256,
				MajorityEnforceBlockUpgrade = 750,
				MajorityRejectBlockOutdated = 950,
				MajorityWindow = 1000,
				PowLimit = new Target(new uint256("7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")),
				PowTargetTimespan = TimeSpan.FromSeconds(3.5 * 24 * 60 * 60),
				PowTargetSpacing = TimeSpan.FromSeconds(2.5 * 60),
				PowAllowMinDifficultyBlocks = true,
				MinimumChainWork = uint256.Zero,
				PowNoRetargeting = true,
				RuleChangeActivationThreshold = 108,
				MinerConfirmationWindow = 144,
				CoinbaseMaturity = 100,
				HashGenesisBlock = new uint256("f5ae71e26c74beacc88382716aced69cddf3dffff24f384e1808905e0188f68f"),
				GetPoWHash = GetPoWHash,
				LitecoinWorkCalculation = true
			})
			.SetBase58Bytes(Base58Type.PUBKEY_ADDRESS, new byte[] { 111 })
			.SetBase58Bytes(Base58Type.SCRIPT_ADDRESS, new byte[] { 196 })
			.SetBase58Bytes(Base58Type.SECRET_KEY, new byte[] { 239 })
			.SetBase58Bytes(Base58Type.EXT_PUBLIC_KEY, new byte[] { 0x04, 0x35, 0x87, 0xCF })
			.SetBase58Bytes(Base58Type.EXT_SECRET_KEY, new byte[] { 0x04, 0x35, 0x83, 0x94 })
			.SetBech32(Bech32Type.WITNESS_PUBKEY_ADDRESS, Encoders.Bech32("tfc"))
			.SetBech32(Bech32Type.WITNESS_SCRIPT_ADDRESS, Encoders.Bech32("tfc"))
			.SetMagic(0xdab5bffa)
			.SetPort(port)
			.SetRPCPort(18447)
			.SetName("ftc-reg")
			.AddAlias("ftc-regtest")
			.AddAlias("feathercoin-reg")
			.AddAlias("feathercoin-regtest")
			.SetGenesis(new Block(Encoders.Hex.DecodeData("010000000000000000000000000000000000000000000000000000000000000000000000d9ced4ed1130f7b7faad9be25323ffafa33232a17c3edf6cfd97bee6bafbdd97dae5494dffff7f20000000000101000000010000000000000000000000000000000000000000000000000000000000000000ffffffff4804ffff001d0104404e592054696d65732030352f4f63742f32303131205374657665204a6f62732c204170706c65e280997320566973696f6e6172792c2044696573206174203536ffffffff0100f2052a010000004341040184710fa689ad5023690c80f3a49c8f13f8d45b8c857fbcbc8bc4a8e4d3eb4b10f4d4604fa08dce601aaf0f470216fe1b51850b4acf21b179c45070ac7b03a9ac00000000"))) //./genesisgen 040184710fa689ad5023690c80f3a49c8f13f8d45b8c857fbcbc8bc4a8e4d3eb4b10f4d4604fa08 dce601aaf0f470216fe1b51850b4acf21b179c45070ac7b03a9 "NY Times 05/Oct/2011 Steve Jobs, Apple’s Visionary, Dies at 56" 486604799
			.BuildAndRegister();

			var home = Environment.GetEnvironmentVariable("HOME");
			var localAppData = Environment.GetEnvironmentVariable("APPDATA");

			if(string.IsNullOrEmpty(home) && string.IsNullOrEmpty(localAppData))
				return new object();

			if(!string.IsNullOrEmpty(home))
			{
				var bitcoinFolder = Path.Combine(home, ".feathercoin");

				var mainnet = Path.Combine(bitcoinFolder, ".cookie");
				RPCClient.RegisterDefaultCookiePath(Networks._Mainnet, mainnet);

				var testnet = Path.Combine(bitcoinFolder, "testnet4", ".cookie");
				RPCClient.RegisterDefaultCookiePath(Networks._Testnet, testnet);

				var regtest = Path.Combine(bitcoinFolder, "regtest", ".cookie");
				RPCClient.RegisterDefaultCookiePath(Networks._Regtest, regtest);
			}
			else if(!string.IsNullOrEmpty(localAppData))
			{
				var bitcoinFolder = Path.Combine(localAppData, "Feathercoin");

				var mainnet = Path.Combine(bitcoinFolder, ".cookie");
				RPCClient.RegisterDefaultCookiePath(Networks._Mainnet, mainnet);

				var testnet = Path.Combine(bitcoinFolder, "testnet4", ".cookie");
				RPCClient.RegisterDefaultCookiePath(Networks._Testnet, testnet);

				var regtest = Path.Combine(bitcoinFolder, "regtest", ".cookie");
				RPCClient.RegisterDefaultCookiePath(Networks._Regtest, regtest);
			}
			return new object();
		}

		static uint256 GetPoWHash(BlockHeader header)
		{
			var headerBytes = header.ToBytes();
			var h = NBitcoin.Crypto.SCrypt.ComputeDerivedKey(headerBytes, headerBytes, 1024, 1, 1, null, 32);
			return new uint256(h);
		}

		private static IEnumerable<NetworkAddress> ToSeed(Tuple<byte[], int>[] tuples)
		{
			return tuples
					.Select(t => new NetworkAddress(new IPAddress(t.Item1), t.Item2))
					.ToArray();
		}

		private static Network _Mainnet;
		public static Network Mainnet
		{
			get
			{
				EnsureRegistered();
				return _Mainnet;
			}
		}

		private static Network _Regtest;
		public static Network Regtest
		{
			get
			{
				EnsureRegistered();
				return _Regtest;
			}
		}

		private static Network _Testnet;
		public static Network Testnet
		{
			get
			{
				EnsureRegistered();
				return _Testnet;
			}
		}
	}
}
