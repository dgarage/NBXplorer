using NBXplorer.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using System;
using System.Linq;
using System.Threading;
using Xunit;
using Xunit.Abstractions;
using NBitcoin.RPC;
using System.Text;
using NBitcoin.Crypto;
using System.Collections.Generic;
using NBXplorer.DerivationStrategy;
using System.Diagnostics;
using NBXplorer.Models;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.IO;
using NBitcoin.Protocol;
using NBXplorer.Configuration;

namespace NBXplorer.Tests
{
	public class UnitTest1
	{
		public UnitTest1(ITestOutputHelper helper)
		{
			Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
			Logs.LogProvider = new XUnitLogProvider(helper);
		}

		[Fact]
		public void RepositoryCanTrackAddresses()
		{
			using(var tester = RepositoryTester.Create(true))
			{
				tester.Repository.Track(DummyPubKey);
				RepositoryCanTrackAddressesCore(tester);

			}
		}

		static DirectDerivationStrategy DummyPubKey = CreateDerivationStrategy();

		private static DirectDerivationStrategy CreateDerivationStrategy(ExtPubKey pubKey = null)
		{
			pubKey = pubKey ?? new ExtKey().Neuter();
			return (DirectDerivationStrategy)new DerivationStrategyFactory(Network.RegTest).Parse($"{pubKey.ToString(Network.RegTest)}-[legacy]");
		}

		private static void RepositoryCanTrackAddressesCore(RepositoryTester tester)
		{
			var keyInfo = tester.Repository.GetKeyInformation(DummyPubKey.GetLineFor(DerivationFeature.Deposit).Derive(0).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("0/0"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy.ToString(), DummyPubKey.ToString());

			keyInfo = tester.Repository.GetKeyInformation(DummyPubKey.GetLineFor(DerivationFeature.Deposit).Derive(1).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("0/1"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy.ToString(), DummyPubKey.ToString());

			keyInfo = tester.Repository.GetKeyInformation(DummyPubKey.GetLineFor(DerivationFeature.Deposit).Derive(29).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("0/29"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy.ToString(), DummyPubKey.ToString());


			keyInfo = tester.Repository.GetKeyInformation(DummyPubKey.GetLineFor(DerivationFeature.Change).Derive(29).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/29"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy.ToString(), DummyPubKey.ToString());

			keyInfo = tester.Repository.GetKeyInformation(DummyPubKey.GetLineFor(DerivationFeature.Deposit).Derive(30).ScriptPubKey);
			Assert.Null(keyInfo);
			keyInfo = tester.Repository.GetKeyInformation(DummyPubKey.GetLineFor(DerivationFeature.Change).Derive(30).ScriptPubKey);
			Assert.Null(keyInfo);

			tester.Repository.MarkAsUsed(CreateKeyPathInformation(DummyPubKey, new KeyPath("1/5")));
			keyInfo = tester.Repository.GetKeyInformation(DummyPubKey.GetLineFor(DerivationFeature.Change).Derive(25).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/25"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy, DummyPubKey);

			keyInfo = tester.Repository.GetKeyInformation(DummyPubKey.GetLineFor(DerivationFeature.Change).Derive(36).ScriptPubKey);
			Assert.Null(keyInfo);

			keyInfo = tester.Repository.GetKeyInformation(DummyPubKey.GetLineFor(DerivationFeature.Deposit).Derive(30).ScriptPubKey);
			Assert.Null(keyInfo);

			for(int i = 0; i < 10; i++)
			{
				tester.Repository.MarkAsUsed(CreateKeyPathInformation(DummyPubKey, new KeyPath("1/" + i)));
			}
			keyInfo = tester.Repository.GetKeyInformation(DummyPubKey.GetLineFor(DerivationFeature.Deposit).Derive(30).ScriptPubKey);
			Assert.Null(keyInfo);
			tester.Repository.MarkAsUsed(CreateKeyPathInformation(DummyPubKey, new KeyPath("1/10")));
			keyInfo = tester.Repository.GetKeyInformation(DummyPubKey.GetLineFor(DerivationFeature.Deposit).Derive(30).ScriptPubKey);
			Assert.NotNull(keyInfo);

			keyInfo = tester.Repository.GetKeyInformation(DummyPubKey.GetLineFor(DerivationFeature.Deposit).Derive(39).ScriptPubKey);
			Assert.NotNull(keyInfo);

			keyInfo = tester.Repository.GetKeyInformation(DummyPubKey.GetLineFor(DerivationFeature.Deposit).Derive(41).ScriptPubKey);
			Assert.Null(keyInfo);

			//No op
			tester.Repository.MarkAsUsed(CreateKeyPathInformation(DummyPubKey, new KeyPath("1/6")));
			keyInfo = tester.Repository.GetKeyInformation(DummyPubKey.GetLineFor(DerivationFeature.Change).Derive(29).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/29"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy.ToString(), DummyPubKey.ToString());
			keyInfo = tester.Repository.GetKeyInformation(DummyPubKey.GetLineFor(DerivationFeature.Change).Derive(30).ScriptPubKey);
			Assert.Null(keyInfo);
		}

		private static KeyPathInformation[] CreateKeyPathInformation(DirectDerivationStrategy pubKey, KeyPath keyPath)
		{
			return new[] { new KeyPathInformation() { Feature = DerivationFeature.Deposit, DerivationStrategy = pubKey, KeyPath = keyPath } };
		}

		[Fact]
		public void ShouldBlockIfNoChange()
		{
			using(var tester = ServerTester.Create())
			{
				var bob = DummyPubKey;
				var utxo = tester.Client.Sync(bob, null, true);
				Stopwatch watch = new Stopwatch();
				watch.Start();
				var result = tester.Client.Sync(bob, utxo);
				watch.Stop();
				Assert.True(watch.Elapsed > TimeSpan.FromSeconds(10));
			}
		}


		[Fact]
		public void ShowRBFedTransaction()
		{
			using(var tester = ServerTester.Create())
			{
				var bob = DummyPubKey;
				tester.Client.Track(bob);
				var utxo = tester.Client.Sync(bob, null, null, true); //Track things do not wait
				var a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);

				var payment1 = Money.Coins(0.04m);
				var payment2 = Money.Coins(0.08m);

				var tx1 = new uint256(tester.RPC.SendCommand("sendtoaddress", new object[]
				{
					a1.ScriptPubKey.GetDestinationAddress(tester.Network).ToString(),
					payment1.ToString(),
					null, //comment
                    null, //comment_to
                    false, //subtractfeefromamount
                    true, //replaceable
				}).ResultString);

				utxo = tester.Client.Sync(bob, utxo); //Wait tx received
				Assert.Equal(tx1, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);

				var tx = tester.RPC.GetRawTransaction(new uint256(tx1));
				foreach(var input in tx.Inputs)
				{
					input.ScriptSig = Script.Empty; //Strip signatures
				}
				var output = tx.Outputs.First(o => o.Value == payment1);
				output.Value = payment2;
				var change = tx.Outputs.First(o => o.Value != payment1);
				change.Value -= (payment2 - payment1) * 2; //Add more fees
				var replaced = tester.RPC.SignRawTransaction(tx);

				tester.RPC.SendRawTransaction(replaced);

				var prevUtxo = utxo;
				utxo = tester.Client.Sync(bob, prevUtxo); //Wait tx received
				Assert.True(utxo.Unconfirmed.Reset);
				Assert.Equal(replaced.GetHash(), utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				Assert.Single(utxo.Unconfirmed.UTXOs);
			}
		}

		[Fact]
		public void CanGetUnusedAddresses()
		{
			using(var tester = ServerTester.Create())
			{
				var bob = DummyPubKey;
				var utxo = tester.Client.Sync(bob, null, null, true); //Track things do not wait

				var a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);
				Assert.Null(a1);
				tester.Client.Track(bob);
				a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);
				Assert.NotNull(a1);
				Assert.Equal(a1.ScriptPubKey, tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0).ScriptPubKey);
				Assert.Equal(a1.ScriptPubKey, bob.Root.Derive(new KeyPath("0/0")).PubKey.Hash.ScriptPubKey);

				var a2 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, skip: 1);
				Assert.Equal(a2.ScriptPubKey, bob.Root.Derive(new KeyPath("0/1")).PubKey.Hash.ScriptPubKey);

				var a3 = tester.Client.GetUnused(bob, DerivationFeature.Change, skip: 0);
				Assert.Equal(a3.ScriptPubKey, bob.Root.Derive(new KeyPath("1/0")).PubKey.Hash.ScriptPubKey);

				Assert.Null(tester.Client.GetUnused(bob, DerivationFeature.Change, skip: 30));

				a3 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, skip: 2);
				Assert.Equal(new KeyPath("0/2"), a3.KeyPath);

				//   0/0 and 0/2 used
				tester.RPC.SendToAddressAsync(a1.ScriptPubKey.GetDestinationAddress(tester.Network), Money.Coins(1.0m));
				utxo = tester.Client.Sync(bob, utxo); //Wait tx received
				tester.RPC.SendToAddressAsync(a3.ScriptPubKey.GetDestinationAddress(tester.Network), Money.Coins(1.0m));
				utxo = tester.Client.Sync(bob, utxo); //Wait tx received

				a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);
				Assert.Equal(a1.ScriptPubKey, bob.Root.Derive(new KeyPath("0/1")).PubKey.Hash.ScriptPubKey);
				a2 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, skip: 1);
				Assert.Equal(a2.ScriptPubKey, bob.Root.Derive(new KeyPath("0/3")).PubKey.Hash.ScriptPubKey);
			}
		}

		CancellationToken Cancel => new CancellationTokenSource(5000).Token;

		[Fact]
		public void CanUseWebSockets()
		{
			using(var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);
				using(var connected = tester.Client.CreateNotificationSession())
				{
					connected.ListenNewBlock();
					var expectedBlockId = tester.Explorer.CreateRPCClient().Generate(1)[0];
					var blockEvent = (Models.NewBlockEvent)connected.NextEvent(Cancel);
					Assert.Equal(expectedBlockId, blockEvent.Hash);
					Assert.NotEqual(0, blockEvent.Height);

					connected.ListenDerivationSchemes(new[] { pubkey });
					tester.Explorer.CreateRPCClient().SendToAddress(AddressOf(key, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}
			}
		}


		[Fact]
		public void CanTrack4()
		{
			using(var tester = ServerTester.Create())
			{
				var bob = new BitcoinExtKey(new ExtKey(), tester.Network);
				var alice = new BitcoinExtKey(new ExtKey(), tester.Network);

				var bobPubKey = CreateDerivationStrategy(bob.Neuter());
				var alicePubKey = CreateDerivationStrategy(alice.Neuter());

				tester.Client.Track(alicePubKey);
				var utxoAlice = tester.Client.Sync(alicePubKey, uint256.Zero, uint256.Zero, true); //Track things do not wait
				tester.Client.Track(bobPubKey);
				var utxoBob = tester.Client.Sync(bobPubKey, null, null, true); //Track things do not wait
				Assert.False(utxoAlice.Confirmed.Reset);
				Assert.False(utxoAlice.Unconfirmed.Reset);

				var id = tester.RPC.SendToAddress(AddressOf(alice, "0/1"), Money.Coins(1.0m));
				id = tester.RPC.SendToAddress(AddressOf(bob, "0/2"), Money.Coins(0.1m));
				utxoAlice = tester.Client.Sync(alicePubKey, utxoAlice);
				utxoBob = tester.Client.Sync(bobPubKey, utxoBob);
				Assert.False(utxoAlice.Unconfirmed.Reset);

				tester.RPC.Generate(1);

				utxoAlice = tester.Client.Sync(alicePubKey, utxoAlice);
				utxoBob = tester.Client.Sync(bobPubKey, utxoBob);
				Assert.False(utxoAlice.Confirmed.Reset);

				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(PrivateKeyOf(alice, "0/1"));
				tester.RPC.SendToAddress(AddressOf(bob, "0/3"), Money.Coins(0.6m));

				utxoAlice = tester.Client.Sync(alicePubKey, utxoAlice);
				utxoBob = tester.Client.Sync(bobPubKey, utxoBob);
				Assert.False(utxoAlice.Unconfirmed.Reset);

				utxoAlice = tester.Client.Sync(alicePubKey, utxoAlice, true);
				Assert.False(utxoAlice.Unconfirmed.Reset);

				tester.RPC.Generate(1);

				utxoAlice = tester.Client.Sync(alicePubKey, utxoAlice);
				utxoBob = tester.Client.Sync(bobPubKey, utxoBob);

				Assert.False(utxoAlice.Confirmed.Reset);
				Assert.Single(utxoAlice.Confirmed.SpentOutpoints);
				Assert.Empty(utxoAlice.Confirmed.UTXOs);

				Assert.Empty(utxoBob.Confirmed.SpentOutpoints);
				Assert.Single(utxoBob.Confirmed.UTXOs);
				Assert.Equal("0/3", utxoBob.Confirmed.UTXOs[0].KeyPath.ToString());
			}
		}
		[Fact]
		public void CanTrack3()
		{
			using(var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);
				tester.Client.Sync(pubkey, null, null, true); //Track things do not wait
				var id = tester.RPC.SendToAddress(AddressOf(key, "0/0"), Money.Coins(1.0m));
				id = tester.RPC.SendToAddress(AddressOf(key, "0/1"), Money.Coins(1.1m));
				id = tester.RPC.SendToAddress(AddressOf(key, "0/2"), Money.Coins(1.2m));

				UTXOChanges utxo = null;

				while(utxo == null || utxo.Unconfirmed.UTXOs.Count != 3)
				{
					utxo = tester.Client.Sync(pubkey, null, null);
				}

				tester.RPC.Generate(1);

				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.True(utxo.HasChanges);
				Assert.Equal(3, utxo.Confirmed.UTXOs.Count);
				utxo = tester.Client.Sync(pubkey, utxo, true);
				Assert.False(utxo.HasChanges);
			}
		}


		[Fact]
		public void CanTrackSeveralTransactions()
		{
			using(var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);
				var utxo = tester.Client.Sync(pubkey, null, null, true); //Track things do not wait

				var addresses = new HashSet<Script>();
				tester.RPC.ImportPrivKey(PrivateKeyOf(key, "0/0"));
				var id = tester.RPC.SendToAddress(AddressOf(key, "0/0"), Money.Coins(1.0m));
				addresses.Add(AddressOf(key, "0/0").ScriptPubKey);

				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.True(utxo.HasChanges);

				var coins = Money.Coins(1.0m);
				int i = 0;
				for(i = 0; i < 20; i++)
				{
					LockTestCoins(tester.RPC, addresses);
					var spendable = tester.RPC.ListUnspent(0, 0);
					coins = coins - Money.Coins(0.001m);
					var destination = AddressOf(key, $"0/{i + 1}");

					tester.RPC.ImportPrivKey(PrivateKeyOf(key, $"0/{i + 1}"));
					tester.RPC.SendToAddress(destination, coins);
					addresses.Add(destination.ScriptPubKey);
				}

				while(true)
				{
					utxo = tester.Client.Sync(pubkey, utxo);
					if(!utxo.HasChanges)
						Assert.False(true, "should have changes");
					Assert.False(utxo.Confirmed.Reset);
					Assert.True(utxo.Unconfirmed.HasChanges);
					Assert.Single(utxo.Unconfirmed.UTXOs);
					if(new KeyPath($"0/{i}").Equals(utxo.Unconfirmed.UTXOs[0].KeyPath))
						break;
				}

				tester.RPC.Generate(1);

				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.True(utxo.Confirmed.HasChanges);
				Assert.Empty(utxo.Confirmed.SpentOutpoints);
			}
		}

		[Fact]
		public void CanTrack2()
		{
			using(var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);
				var utxo = tester.Client.Sync(pubkey, null, null, true); //Track things do not wait
				var tx1 = tester.RPC.SendToAddress(AddressOf(key, "0/0"), Money.Coins(1.0m));
				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.False(utxo.Confirmed.Reset);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(tx1, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);


				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(PrivateKeyOf(key, "0/0"));
				var tx2 = tester.RPC.SendToAddress(AddressOf(key, "1/0"), Money.Coins(0.6m));

				var prevUtxo = utxo;
				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.False(utxo.Unconfirmed.Reset);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(tx2, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash); //got the 0.6m
				Assert.Equal(Money.Coins(0.6m), utxo.Unconfirmed.UTXOs[0].Value); //got the 0.6m

				Assert.Single(utxo.Unconfirmed.SpentOutpoints);
				Assert.Equal(tx1, utxo.Unconfirmed.SpentOutpoints[0].Hash); //previous coin is spent

				utxo = tester.Client.Sync(pubkey, prevUtxo.Confirmed.Hash, null);
				Assert.True(utxo.Unconfirmed.Reset);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Empty(utxo.Unconfirmed.SpentOutpoints); //should be skipped as the unconf coin were not known

				tester.RPC.SendToAddress(AddressOf(key, "0/0"), Money.Coins(0.15m));

				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(Money.Coins(0.15m), utxo.Unconfirmed.UTXOs[0].Value);
				Assert.Empty(utxo.Unconfirmed.SpentOutpoints);

				utxo = tester.Client.Sync(pubkey, null);
				Assert.Equal(2, utxo.Unconfirmed.UTXOs.Count); //Should have 0.15 and 0.6
				Assert.Equal(Money.Coins(0.75m), utxo.Unconfirmed.UTXOs.Select(c => c.Value).Sum());
				Assert.Empty(utxo.Unconfirmed.SpentOutpoints);
			}
		}

		[Fact]
		public void CanReserveAddress()
		{
			using(var tester = ServerTester.Create())
			{
				//WaitServerStarted not needed, just a sanity check
				tester.Client.WaitServerStarted();
				tester.Client.Track(DummyPubKey);
				var utxo = tester.Client.Sync(DummyPubKey, null, null, true); //Track things do not wait

				var tasks = new List<Task<KeyPathInformation>>();
				for(int i = 0; i < 100; i++)
				{
					tasks.Add(tester.Client.GetUnusedAsync(DummyPubKey, DerivationFeature.Deposit, reserve: true));
				}
				Task.WaitAll(tasks.ToArray());

				var paths = tasks.Select(t => t.Result).ToDictionary(c => c.KeyPath);
				Assert.Equal(99U, paths.Select(p => p.Key.Indexes.Last()).Max());

				tester.Client.CancelReservation(DummyPubKey, new[] { new KeyPath("0/0") });
				Assert.Equal(new KeyPath("0/0"), tester.Client.GetUnused(DummyPubKey, DerivationFeature.Deposit).KeyPath);
			}
		}


		[Fact]
		public void CanParseDerivationScheme()
		{
			var network = Network.Main;
			var factory = new DerivationStrategy.DerivationStrategyFactory(network);
			var tata = new BitcoinExtPubKey("xpub661MyMwAqRbcFiadHioAunPTeic3C17HKPABCBvURz3W2ivn63jzEYYXWpDePLGncjLuRvQKx7jrKweSkoEvgQTvAo5zw4z8HPGC8Y4E4Wr", network);
			var toto = new BitcoinExtPubKey("xpub661MyMwAqRbcFqyJE6zy5jMF7bjUtvNHgHJPbENEZtEQKRrukKWJP5xLMKntBaNya7CLMLL6u1KEk8GnrEv8pur5DFSgEMf1hRGjsJrcQKS", network);

			var direct = (DirectDerivationStrategy)factory.Parse($"{toto}-[legacy]");
			var generated = Generate(direct);
			Assert.Equal(toto.ExtPubKey.Derive(new KeyPath("0/1")).PubKey.Hash.ScriptPubKey, generated.ScriptPubKey);
			Assert.Null(generated.Redeem);

			var p2wpkh = (DirectDerivationStrategy)factory.Parse($"{toto}");
			generated = Generate(p2wpkh);
			Assert.Null(generated.Redeem);
			Assert.Equal(toto.ExtPubKey.Derive(new KeyPath("0/1")).PubKey.WitHash.ScriptPubKey, generated.ScriptPubKey);

			var p2shp2wpkh = (P2SHDerivationStrategy)factory.Parse($"{toto}-[p2sh]");
			generated = Generate(p2shp2wpkh);
			Assert.NotNull(generated.Redeem);
			Assert.Equal(toto.ExtPubKey.Derive(new KeyPath("0/1")).PubKey.WitHash.ScriptPubKey.Hash.ScriptPubKey, generated.ScriptPubKey);
			Assert.Equal(toto.ExtPubKey.Derive(new KeyPath("0/1")).PubKey.WitHash.ScriptPubKey, generated.Redeem);

			//Same thing as above, reversed attribute
			p2shp2wpkh = (P2SHDerivationStrategy)factory.Parse($"{toto}-[p2sh]");
			Assert.NotNull(generated.Redeem);
			Assert.Equal(toto.ExtPubKey.Derive(new KeyPath("0/1")).PubKey.WitHash.ScriptPubKey.Hash.ScriptPubKey, generated.ScriptPubKey);
			Assert.Equal(toto.ExtPubKey.Derive(new KeyPath("0/1")).PubKey.WitHash.ScriptPubKey, generated.Redeem);

			var multiSig = (P2SHDerivationStrategy)factory.Parse($"2-of-{toto}-{tata}-[legacy]");
			generated = Generate(multiSig);
			Assert.Equal(new Script("0 025ca59b2007a67f24fdd26acefbe8feb5e8849c207d504b16d4801a8290fe9409 03d15f88de692693e0c25cec27b68da49ae4c29805efbe08154c4acfdf951ccb54 2 OP_CHECKMULTISIG"), generated.Redeem);
			multiSig = (P2SHDerivationStrategy)factory.Parse($"2-of-{toto}-{tata}-[legacy]-[keeporder]");
			generated = Generate(multiSig);
			Assert.Equal(new Script("0 03d15f88de692693e0c25cec27b68da49ae4c29805efbe08154c4acfdf951ccb54 025ca59b2007a67f24fdd26acefbe8feb5e8849c207d504b16d4801a8290fe9409 2 OP_CHECKMULTISIG"), generated.Redeem);

			var multiP2SH = (P2WSHDerivationStrategy)factory.Parse($"2-of-{toto}-{tata}");
			generated = Generate(multiP2SH);
			Assert.IsType<WitScriptId>(generated.ScriptPubKey.GetDestination());
			Assert.NotNull(PayToMultiSigTemplate.Instance.ExtractScriptPubKeyParameters(generated.Redeem));

			var multiP2WSHP2SH = (P2SHDerivationStrategy)factory.Parse($"2-of-{toto}-{tata}-[p2sh]");
			generated = Generate(multiP2WSHP2SH);
			Assert.IsType<ScriptId>(generated.ScriptPubKey.GetDestination());
			Assert.NotNull(PayToMultiSigTemplate.Instance.ExtractScriptPubKeyParameters(generated.Redeem));
		}

		private static Derivation Generate(DerivationStrategyBase strategy)
		{
			return strategy.GetLineFor(DerivationFeature.Deposit).Derive(1);
		}

		[Fact]
		public void CanGetStatus()
		{
			using(var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var status = tester.Client.GetStatus();
				Assert.NotNull(status.BitcoinStatus);
				Assert.True(status.IsFullySynched);
				Assert.Equal(status.BitcoinStatus.Blocks, status.BitcoinStatus.Headers);
				Assert.Equal(status.BitcoinStatus.Blocks, status.ChainHeight);
				Assert.Equal(1.0, status.BitcoinStatus.VerificationProgress);
				Assert.NotNull(status.Version);
				Assert.Equal("BTC", status.CryptoCode);
				Assert.Equal(ChainType.Regtest, status.ChainType);
				Assert.Equal("BTC", status.SupportedCryptoCodes[0]);
				Assert.Single(status.SupportedCryptoCodes);
			}
		}

		public CancellationToken Timeout => new CancellationTokenSource(10000).Token;

		[Fact]
		public void CanTrack()
		{
			using(var tester = ServerTester.Create())
			{
				//WaitServerStarted not needed, just a sanity check
				tester.Client.WaitServerStarted(Timeout);
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = CreateDerivationStrategy(key.Neuter());

				tester.Client.Track(pubkey);
				var utxo = tester.Client.Sync(pubkey, null, null, true); //Track things do not wait
				var gettingUTXO = tester.Client.SyncAsync(pubkey, utxo);
				var txId = tester.RPC.SendToAddress(AddressOf(key, "0/0"), Money.Coins(1.0m));
				utxo = gettingUTXO.GetAwaiter().GetResult();
				Assert.Equal(103, utxo.CurrentHeight);

				Assert.False(utxo.Confirmed.Reset);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(txId, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				var unconfTimestamp = utxo.Unconfirmed.UTXOs[0].Timestamp;
				Assert.Equal(0, utxo.Unconfirmed.UTXOs[0].Confirmations);
				Assert.Empty(utxo.Confirmed.UTXOs);
				Assert.Equal(uint256.Zero, utxo.Confirmed.Hash);
				Assert.NotEqual(uint256.Zero, utxo.Unconfirmed.Hash);

				var tx = tester.Client.GetTransaction(utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				Assert.NotNull(tx);
				Assert.Equal(unconfTimestamp, tx.Timestamp);
				Assert.Equal(0, tx.Confirmations);
				Assert.Equal(utxo.Unconfirmed.UTXOs[0].Outpoint.Hash, tx.Transaction.GetHash());

				tester.RPC.Generate(1);
				var prevUtxo = utxo;
				utxo = tester.Client.Sync(pubkey, prevUtxo);
				Assert.True(utxo.Unconfirmed.Reset);
				Assert.Empty(utxo.Unconfirmed.UTXOs);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(txId, utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.Equal(1, utxo.Confirmed.UTXOs[0].Confirmations);
				Assert.Equal(unconfTimestamp, utxo.Confirmed.UTXOs[0].Timestamp);
				Assert.NotEqual(uint256.Zero, utxo.Confirmed.Hash);
				var prevConfHash = utxo.Confirmed.Hash;

				txId = tester.RPC.SendToAddress(AddressOf(key, "0/1"), Money.Coins(1.0m));
				var txId1 = txId;

				prevUtxo = utxo;
				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.Empty(utxo.Confirmed.UTXOs);
				Assert.False(utxo.Confirmed.Reset);
				Assert.True(utxo.HasChanges);
				Assert.False(utxo.Unconfirmed.Reset);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(txId, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				utxo = tester.Client.Sync(pubkey, null, null, true);

				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(new KeyPath("0/1"), utxo.Unconfirmed.UTXOs[0].KeyPath);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(new KeyPath("0/0"), utxo.Confirmed.UTXOs[0].KeyPath);
				Assert.Equal(prevConfHash, utxo.Confirmed.Hash);

				utxo = tester.Client.Sync(pubkey, utxo.Confirmed.Hash, null, true);
				Assert.False(utxo.Confirmed.Reset);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Empty(utxo.Confirmed.UTXOs);

				utxo = tester.Client.Sync(pubkey, null, utxo.Unconfirmed.Hash, true);
				Assert.True(utxo.Confirmed.Reset);
				Assert.Empty(utxo.Unconfirmed.UTXOs);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(1, utxo.Confirmed.UTXOs[0].Confirmations);

				Assert.Null(tester.Client.GetTransaction(uint256.One));
				tx = tester.Client.GetTransaction(utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.NotNull(tx);
				Assert.Equal(unconfTimestamp, tx.Timestamp);
				Assert.Equal(1, tx.Confirmations);
				Assert.Equal(utxo.Confirmed.UTXOs[0].Outpoint.Hash, tx.Transaction.GetHash());
				tester.RPC.Generate(1);

				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(new KeyPath("0/1"), utxo.Confirmed.UTXOs[0].KeyPath);

				var outpoint01 = utxo.Confirmed.UTXOs[0].Outpoint;

				txId = tester.RPC.SendToAddress(AddressOf(key, "0/2"), Money.Coins(1.0m));
				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Empty(utxo.Confirmed.UTXOs);
				Assert.Equal(new KeyPath("0/2"), utxo.Unconfirmed.UTXOs[0].KeyPath);
				tester.RPC.Generate(1);

				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(new KeyPath("0/2"), utxo.Confirmed.UTXOs[0].KeyPath);


				utxo = tester.Client.Sync(pubkey, utxo, true);
				Assert.True(!utxo.HasChanges);

				var before01Spend = utxo.Confirmed.Hash;

				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(PrivateKeyOf(key, "0/1"));
				txId = tester.RPC.SendToAddress(AddressOf(key, "0/3"), Money.Coins(0.5m));

				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(new KeyPath("0/3"), utxo.Unconfirmed.UTXOs[0].KeyPath);
				Assert.Single(utxo.Unconfirmed.SpentOutpoints); // "0/1" should be spent
				Assert.Equal(txId1, utxo.Unconfirmed.SpentOutpoints[0].Hash); // "0/1" should be spent

				utxo = tester.Client.Sync(pubkey, utxo, true);
				Assert.False(utxo.HasChanges);
				tester.RPC.Generate(1);

				utxo = tester.Client.Sync(pubkey, before01Spend, utxo.Unconfirmed.Hash);
				Assert.True(utxo.Unconfirmed.HasChanges);

				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(new KeyPath("0/3"), utxo.Confirmed.UTXOs[0].KeyPath);
				Assert.Single(utxo.Confirmed.SpentOutpoints);
				Assert.Equal(outpoint01, utxo.Confirmed.SpentOutpoints[0]);

				utxo = tester.Client.Sync(pubkey, utxo, true);
				Assert.False(utxo.HasChanges);
			}
		}

		private void LockTestCoins(RPCClient rpc, HashSet<Script> keepAddresses = null)
		{
			if(keepAddresses == null)
			{
				var outpoints = rpc.ListUnspent().Where(l => l.Address == null).Select(o => o.OutPoint).ToArray();
				rpc.LockUnspent(outpoints);
			}
			else
			{
				var outpoints = rpc.ListUnspent(0, 999999).Where(l => !keepAddresses.Contains(l.ScriptPubKey)).Select(c => c.OutPoint).ToArray();
				rpc.LockUnspent(outpoints);
			}
		}

		private BitcoinSecret PrivateKeyOf(BitcoinExtKey key, string path)
		{
			return new BitcoinSecret(key.ExtKey.Derive(new KeyPath(path)).PrivateKey, key.Network);
		}

		private BitcoinAddress AddressOf(BitcoinExtKey key, string path)
		{
			return key.ExtKey.Derive(new KeyPath(path)).Neuter().PubKey.Hash.GetAddress(key.Network);
		}

		[Fact]
		public void CanTopologicalSort()
		{
			var arr = Enumerable.Range(0, 100).ToArray();
			var expected = arr.ToArray();
			NBitcoin.Utils.Shuffle(arr);
			var actual = arr.TopologicalSort(o => arr.Where(a => a < o)).ToArray();
			Assert.True(expected.SequenceEqual(actual));
		}

		[Fact]
		public void CanTopologicalSortTx()
		{
			var tx1 = new Transaction() { Outputs = { new TxOut(Money.Zero, new Key()) } };
			var tx2 = new Transaction() { Inputs = { new TxIn(new OutPoint(tx1, 0)) } };
			var tx3 = new Transaction() { Inputs = { new TxIn(new OutPoint(tx2, 0)) } };

			var arr = new[] { tx2, tx1, tx3 };
			var expected = new[] { tx1, tx2, tx3 };
			var actual = arr.TopologicalSort().ToArray();
			Assert.True(expected.SequenceEqual(actual));
		}

		[Fact]
		public void CanBroadcast()
		{
			using(var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var tx = new Transaction();
				tx.Outputs.Add(new TxOut(Money.Coins(1.0m), new Key()));
				var funded = tester.User1.CreateRPCClient().FundRawTransaction(tx);
				var signed = tester.User1.CreateRPCClient().SignRawTransaction(funded.Transaction);
				var result = tester.Client.Broadcast(signed);
				Assert.True(result.Success);
				signed.Inputs[0].PrevOut.N = 999;
				result = tester.Client.Broadcast(signed);
				Assert.False(result.Success);

				var ex = Assert.Throws<NBXplorerException>(() => tester.Client.GetFeeRate(5));
				Assert.Equal("fee-estimation-unavailable", ex.Error.Code);
			}
		}
	}
}
