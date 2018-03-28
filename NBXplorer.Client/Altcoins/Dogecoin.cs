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

namespace NBXplorer.Altcoins.Dogecoin
{
	// Reference: https://github.com/dogecoin/dogecoin/blob/10a5e93a055ab5f239c5447a5fe05283af09e293/src/chainparams.cpp
	public class Networks
	{
		//Format visual studio
		//{({.*?}), (.*?)}
		//Tuple.Create(new byte[]$1, $2)
		//static Tuple<byte[], int>[] pnSeed6_main = null;
		//static Tuple<byte[], int>[] pnSeed6_test = null;
		// Not used in DOGE: https://github.com/dogecoin/dogecoin/blob/10a5e93a055ab5f239c5447a5fe05283af09e293/src/chainparams.cpp#L135

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
			NetworkBuilder builder = new NetworkBuilder();
			_Mainnet = builder.SetConsensus(new Consensus()
			{
				SubsidyHalvingInterval = 100000,
				MajorityEnforceBlockUpgrade = 1500,
				MajorityRejectBlockOutdated = 1900,
				MajorityWindow = 2000,
				PowLimit = new Target(new uint256("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")),
				PowTargetTimespan = TimeSpan.FromSeconds(4 * 60 * 60),
				PowTargetSpacing = TimeSpan.FromSeconds(60),
				PowAllowMinDifficultyBlocks = false,
				CoinbaseMaturity = 30,
				//
				PowNoRetargeting = false, // Not set in reference client, assuming false
				//RuleChangeActivationThreshold = 6048,
				//MinerConfirmationWindow = 8064,
				HashGenesisBlock = new uint256("1a91e3dace36e2be3bf030a65679fe821aa1d6ef92e7c9902eb318182c355691"),
				GetPoWHash = GetPoWHash,
				LitecoinWorkCalculation = true,
			})
			.SetBase58Bytes(Base58Type.PUBKEY_ADDRESS, new byte[] { 30 })
			.SetBase58Bytes(Base58Type.SCRIPT_ADDRESS, new byte[] { 22 })
			.SetBase58Bytes(Base58Type.SECRET_KEY, new byte[] { 158 })
			.SetBase58Bytes(Base58Type.EXT_PUBLIC_KEY, new byte[] { 0x02, 0xFA, 0xCA, 0xFD })
			.SetBase58Bytes(Base58Type.EXT_SECRET_KEY, new byte[] { 0x02, 0xFA, 0xC3, 0x98 })
			.SetBech32(Bech32Type.WITNESS_PUBKEY_ADDRESS, Encoders.Bech32("doge"))
			.SetBech32(Bech32Type.WITNESS_SCRIPT_ADDRESS, Encoders.Bech32("doge"))
			.SetMagic(0xc0c0c0c0)
			.SetPort(22556)
			.SetRPCPort(22555)
			.SetName("doge-main")
			.AddAlias("doge-mainnet")
			.AddAlias("dogecoin-mainnet")
			.AddAlias("dogecoin-main")
			.AddDNSSeeds(new[]
			{
				new DNSSeedData("dogecoin.com", "seed.dogecoin.com"),
				new DNSSeedData("multidoge.org", "seed.multidoge.org"),
				new DNSSeedData("multidoge.org", "seed.multidoge.org"),
				new DNSSeedData("doger.dogecoin.com", "seed.doger.dogecoin.com")
			})
			.AddSeeds(new NetworkAddress[0])
			.SetGenesis(new Block(Encoders.Hex.DecodeData("NEED TO OBTAIN IT")))
			.BuildAndRegister();

			builder = new NetworkBuilder();
			_Testnet = builder.SetConsensus(new Consensus()
			{
				SubsidyHalvingInterval = 840000,
				MajorityEnforceBlockUpgrade = 51,
				MajorityRejectBlockOutdated = 75,
				MajorityWindow = 1000,
				PowLimit = new Target(new uint256("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")),
				PowTargetTimespan = TimeSpan.FromSeconds(3.5 * 24 * 60 * 60),
				PowTargetSpacing = TimeSpan.FromSeconds(2.5 * 60),
				PowAllowMinDifficultyBlocks = true,
				PowNoRetargeting = false,
				RuleChangeActivationThreshold = 1512,
				MinerConfirmationWindow = 2016,
				CoinbaseMaturity = 100,
				HashGenesisBlock = new uint256("4966625a4b2851d9fdee139e56211a0d88575f59ed816ff5e6a63deb4e3e29a0"),
				GetPoWHash = GetPoWHash,
				LitecoinWorkCalculation = true
			})
			.SetBase58Bytes(Base58Type.PUBKEY_ADDRESS, new byte[] { 111 })
			.SetBase58Bytes(Base58Type.SCRIPT_ADDRESS, new byte[] { 58 })
			.SetBase58Bytes(Base58Type.SECRET_KEY, new byte[] { 239 })
			.SetBase58Bytes(Base58Type.EXT_PUBLIC_KEY, new byte[] { 0x04, 0x35, 0x87, 0xCF })
			.SetBase58Bytes(Base58Type.EXT_SECRET_KEY, new byte[] { 0x04, 0x35, 0x83, 0x94 })
			.SetBech32(Bech32Type.WITNESS_PUBKEY_ADDRESS, Encoders.Bech32("tltc"))
			.SetBech32(Bech32Type.WITNESS_SCRIPT_ADDRESS, Encoders.Bech32("tltc"))
			.SetMagic(0xf1c8d2fd)
			.SetPort(19335)
			.SetRPCPort(19332)
			.SetName("ltc-test")
			.AddAlias("ltc-testnet")
			.AddAlias("litecoin-test")
			.AddAlias("litecoin-testnet")
			.AddDNSSeeds(new[]
			{
				new DNSSeedData("litecointools.com", "testnet-seed.litecointools.com"),
				new DNSSeedData("loshan.co.uk", "seed-b.litecoin.loshan.co.uk"),
				new DNSSeedData("thrasher.io", "dnsseed-testnet.thrasher.io"),
			})
			.AddSeeds(new NetworkAddress[0])
			.SetGenesis(new Block(Encoders.Hex.DecodeData("010000000000000000000000000000000000000000000000000000000000000000000000d9ced4ed1130f7b7faad9be25323ffafa33232a17c3edf6cfd97bee6bafbdd97f60ba158f0ff0f1ee17904000101000000010000000000000000000000000000000000000000000000000000000000000000ffffffff4804ffff001d0104404e592054696d65732030352f4f63742f32303131205374657665204a6f62732c204170706c65e280997320566973696f6e6172792c2044696573206174203536ffffffff0100f2052a010000004341040184710fa689ad5023690c80f3a49c8f13f8d45b8c857fbcbc8bc4a8e4d3eb4b10f4d4604fa08dce601aaf0f470216fe1b51850b4acf21b179c45070ac7b03a9ac00000000")))
			.BuildAndRegister();

			builder = new NetworkBuilder();
			_Regtest = builder.SetConsensus(new Consensus()
			{
				SubsidyHalvingInterval = 150,
				MajorityEnforceBlockUpgrade = 51,
				MajorityRejectBlockOutdated = 75,
				MajorityWindow = 144,
				PowLimit = new Target(new uint256("7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")),
				PowTargetTimespan = TimeSpan.FromSeconds(3.5 * 24 * 60 * 60),
				PowTargetSpacing = TimeSpan.FromSeconds(2.5 * 60),
				PowAllowMinDifficultyBlocks = true,
				MinimumChainWork = uint256.Zero,
				PowNoRetargeting = true,
				RuleChangeActivationThreshold = 108,
				MinerConfirmationWindow = 2016,
				CoinbaseMaturity = 100,
				HashGenesisBlock = new uint256("f5ae71e26c74beacc88382716aced69cddf3dffff24f384e1808905e0188f68f"),
				GetPoWHash = GetPoWHash,
				LitecoinWorkCalculation = true
			})
			.SetBase58Bytes(Base58Type.PUBKEY_ADDRESS, new byte[] { 111 })
			.SetBase58Bytes(Base58Type.SCRIPT_ADDRESS, new byte[] { 58 })
			.SetBase58Bytes(Base58Type.SECRET_KEY, new byte[] { 239 })
			.SetBase58Bytes(Base58Type.EXT_PUBLIC_KEY, new byte[] { 0x04, 0x35, 0x87, 0xCF })
			.SetBase58Bytes(Base58Type.EXT_SECRET_KEY, new byte[] { 0x04, 0x35, 0x83, 0x94 })
			.SetBech32(Bech32Type.WITNESS_PUBKEY_ADDRESS, Encoders.Bech32("tltc"))
			.SetBech32(Bech32Type.WITNESS_SCRIPT_ADDRESS, Encoders.Bech32("tltc"))
			.SetMagic(0xdab5bffa)
			.SetPort(19444)
			.SetRPCPort(19332)
			.SetName("ltc-reg")
			.AddAlias("ltc-regtest")
			.AddAlias("litecoin-reg")
			.AddAlias("litecoin-regtest")
			.SetGenesis(new Block(Encoders.Hex.DecodeData("010000000000000000000000000000000000000000000000000000000000000000000000d9ced4ed1130f7b7faad9be25323ffafa33232a17c3edf6cfd97bee6bafbdd97dae5494dffff7f20000000000101000000010000000000000000000000000000000000000000000000000000000000000000ffffffff4804ffff001d0104404e592054696d65732030352f4f63742f32303131205374657665204a6f62732c204170706c65e280997320566973696f6e6172792c2044696573206174203536ffffffff0100f2052a010000004341040184710fa689ad5023690c80f3a49c8f13f8d45b8c857fbcbc8bc4a8e4d3eb4b10f4d4604fa08dce601aaf0f470216fe1b51850b4acf21b179c45070ac7b03a9ac00000000")))
			.BuildAndRegister();

			var home = Environment.GetEnvironmentVariable("HOME");
			var localAppData = Environment.GetEnvironmentVariable("APPDATA");

			if(string.IsNullOrEmpty(home) && string.IsNullOrEmpty(localAppData))
				return new object();

			if(!string.IsNullOrEmpty(home))
			{
				var bitcoinFolder = Path.Combine(home, ".litecoin");

				var mainnet = Path.Combine(bitcoinFolder, ".cookie");
				RPCClient.RegisterDefaultCookiePath(Networks._Mainnet, mainnet);

				var testnet = Path.Combine(bitcoinFolder, "testnet4", ".cookie");
				RPCClient.RegisterDefaultCookiePath(Networks._Testnet, testnet);

				var regtest = Path.Combine(bitcoinFolder, "regtest", ".cookie");
				RPCClient.RegisterDefaultCookiePath(Networks._Regtest, regtest);
			}
			else if(!string.IsNullOrEmpty(localAppData))
			{
				var bitcoinFolder = Path.Combine(localAppData, "Litecoin");

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
