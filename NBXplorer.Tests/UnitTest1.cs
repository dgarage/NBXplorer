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
using Newtonsoft.Json.Linq;

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
		public async Task RepositoryCanTrackAddresses()
		{
			using(var tester = RepositoryTester.Create(true))
			{
				var dummy = new DirectDerivationStrategy(new ExtKey().Neuter().GetWif(Network.RegTest)) { Segwit = false };
				await tester.Repository.Track(dummy);
				await RepositoryCanTrackAddressesCore(tester, dummy);
			}
		}

		[Fact]
		public async Task RepositoryCanLock()
		{
			using(var tester = RepositoryTester.Create(true))
			{
				var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
				var dummy = new DirectDerivationStrategy(new ExtKey().Neuter().GetWif(Network.RegTest)) { Segwit = false };
				var l = await tester.Repository.TakeWalletLock(dummy, timeout);

				var fastTimeout = new CancellationTokenSource(1).Token;
				Assert.Throws<TaskCanceledException>(() => tester.Repository.TakeWalletLock(dummy, fastTimeout).GetAwaiter().GetResult());

				Assert.True(await l.ReleaseLock());
				Assert.False(await l.ReleaseLock());

				l = await tester.Repository.TakeWalletLock(dummy, timeout);
				Assert.Throws<TaskCanceledException>(() => tester.Repository.TakeWalletLock(dummy, fastTimeout).GetAwaiter().GetResult());

				Assert.True(await l.ReleaseLock());

				l = await tester.Repository.TakeWalletLock(dummy, timeout);

				var takingLock = tester.Repository.TakeWalletLock(dummy, timeout);

				await Task.Delay(1000);

				Assert.True(await l.ReleaseLock());

				l = await takingLock;
				Assert.True(await l.ReleaseLock());
			}
		}

		[Fact]
		public void CanSerializeKeyPathFast()
		{
			using(var tester = RepositoryTester.Create(true))
			{
				var dummy = new DirectDerivationStrategy(new ExtKey().Neuter().GetWif(Network.RegTest)) { Segwit = false };
				var seria = new Serializer(Network.RegTest);
				var keyInfo = new KeyPathInformation() { DerivationStrategy = dummy, Feature = DerivationFeature.Change, KeyPath = new KeyPath("0/1"), Redeem = Script.Empty, ScriptPubKey = Script.Empty };
				var str = seria.ToString(keyInfo);
				for(int i = 0; i < 1500; i++)
				{
					seria.ToObject<KeyPathInformation>(str);
				}
			}
		}

		private static async Task RepositoryCanTrackAddressesCore(RepositoryTester tester, DerivationStrategyBase dummy)
		{
			var keyInfo = await tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(0).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("0/0"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy.ToString(), dummy.ToString());

			keyInfo = await tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(1).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("0/1"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy.ToString(), dummy.ToString());

			keyInfo = await tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(29).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("0/29"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy.ToString(), dummy.ToString());


			keyInfo = await tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Change).Derive(29).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/29"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy.ToString(), dummy.ToString());

			keyInfo = await tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(30).ScriptPubKey);
			Assert.Null(keyInfo);
			keyInfo = await tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Change).Derive(30).ScriptPubKey);
			Assert.Null(keyInfo);

			await tester.Repository.MarkAsUsed(CreateKeyPathInformation((DirectDerivationStrategy)dummy, new KeyPath("1/5")));
			keyInfo = await tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Change).Derive(25).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/25"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy, dummy);

			keyInfo = await tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Change).Derive(36).ScriptPubKey);
			Assert.Null(keyInfo);

			keyInfo = await tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(30).ScriptPubKey);
			Assert.Null(keyInfo);

			for(int i = 0; i < 10; i++)
			{
				await tester.Repository.MarkAsUsed(CreateKeyPathInformation((DirectDerivationStrategy)dummy, new KeyPath("1/" + i)));
			}
			keyInfo = await tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(30).ScriptPubKey);
			Assert.Null(keyInfo);
			await tester.Repository.MarkAsUsed(CreateKeyPathInformation((DirectDerivationStrategy)dummy, new KeyPath("1/10")));
			keyInfo = await tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(30).ScriptPubKey);
			Assert.NotNull(keyInfo);

			keyInfo = await tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(39).ScriptPubKey);
			Assert.NotNull(keyInfo);

			keyInfo = await tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(41).ScriptPubKey);
			Assert.Null(keyInfo);

			//No op
			await tester.Repository.MarkAsUsed(CreateKeyPathInformation((DirectDerivationStrategy)dummy, new KeyPath("1/6")));
			keyInfo = await tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Change).Derive(29).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/29"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy.ToString(), dummy.ToString());
			keyInfo = await tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Change).Derive(30).ScriptPubKey);
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
				var bob = tester.CreateDerivationStrategy();
				var utxo = tester.Client.GetUTXOs(bob, null, false);
				Stopwatch watch = new Stopwatch();
				watch.Start();
				var result = tester.Client.GetUTXOs(bob, utxo);
				watch.Stop();
				Assert.True(watch.Elapsed > TimeSpan.FromSeconds(10));
			}
		}

		[Fact]
		public void CanSpendFromUnconfirmed()
		{
			using(var tester = ServerTester.Create())
			{
				var satoshi = new Key().GetWif(tester.Network).GetAddress();
				var aliceExtKey = new ExtKey();
				var bobExtKey = new ExtKey();

				var alice = tester.Client.Network.DerivationStrategyFactory.CreateDirectDerivationStrategy(aliceExtKey.Neuter(), new DerivationStrategyOptions() { P2SH = true });
				tester.Client.Track(alice);

				var bob = tester.Client.Network.DerivationStrategyFactory.CreateDirectDerivationStrategy(bobExtKey.Neuter(), new DerivationStrategyOptions() { P2SH = true });
				tester.Client.Track(bob);

				var aliceUtxo = tester.Client.GetUTXOs(alice, null);
				// Send two coins of 1 BTC to Alice
				var aliceAddress = tester.Client.GetUnused(alice, DerivationFeature.Direct, reserve: true);
				tester.Explorer.CreateRPCClient().SendToAddress(aliceAddress.ScriptPubKey.GetDestinationAddress(tester.Network), Money.Coins(1.0m));
				aliceAddress = tester.Client.GetUnused(alice, DerivationFeature.Direct, reserve: true);
				tester.Explorer.CreateRPCClient().SendToAddress(aliceAddress.ScriptPubKey.GetDestinationAddress(tester.Network), Money.Coins(1.0m));
				aliceUtxo = tester.Client.GetUTXOs(alice, aliceUtxo);

				var aliceBalance = tester.Client.GetBalance(alice);
				Assert.Equal(Money.Coins(2.0m), aliceBalance.Total);
				Assert.Equal(Money.Coins(2.0m), aliceBalance.Spendable);

				// Alice send 0.3 to bob
				var bobAddress = tester.Client.GetUnused(bob, DerivationFeature.Direct, reserve: true);
				var tx = tester.Client.LockUTXOs(alice, new LockUTXOsRequest() { Amount = Money.Coins(0.3m), Destination = bobAddress.Address, FeeRate = new FeeRate(100, 1) });

				// Only locked, bob has nothing
				var bobBalance = tester.Client.GetBalance(bob);
				Assert.Equal(Money.Coins(0.0m), bobBalance.Total);
				Assert.Equal(Money.Coins(0.0m), bobBalance.Spendable);
				aliceBalance = tester.Client.GetBalance(alice);
				Assert.True(aliceBalance.Spendable.Almost(Money.Coins(1.0m), 0.01m));
				Assert.True(aliceBalance.Total.Almost(Money.Coins(1.7m), 0.01m));

				var signed = tx.Sign(alice, aliceExtKey);
				var broadcast = tester.Client.Broadcast(signed);
				Assert.True(broadcast.Success);

				while(true)
				{
					aliceUtxo = tester.Client.GetUTXOs(alice, aliceUtxo);
					if(aliceUtxo.Unconfirmed.UTXOs.Any(u => u.Outpoint.Hash == signed.GetHash()))
						break;
				}

				// Now it is broadcasted, bob has 0.3
				bobBalance = tester.Client.GetBalance(bob);
				Assert.Equal(Money.Coins(0.3m), bobBalance.Spendable);
				Assert.Equal(Money.Coins(0.3m), bobBalance.Total);

				// And alice 1.7 minus fees
				aliceBalance = tester.Client.GetBalance(alice);
				Assert.True(aliceBalance.Spendable.Almost(Money.Coins(1.7m), 0.01m));
				Assert.True(aliceBalance.Total.Almost(Money.Coins(1.7m), 0.01m));
			}
		}

		[Fact]
		public void CanLockUTXOs()
		{
			using(var tester = ServerTester.Create())
			{
				var bob = new Key().GetWif(tester.Network).GetAddress();
				var aliceExtKey = new ExtKey();
				var alice = tester.Client.Network.DerivationStrategyFactory.CreateDirectDerivationStrategy(aliceExtKey.Neuter(), new DerivationStrategyOptions() { P2SH = true });
				tester.Client.Track(alice);

				var utxo = tester.Client.GetUTXOs(alice, null);
				// Send two coins of 1 BTC
				var newAddress = tester.Client.GetUnused(alice, DerivationFeature.Direct, reserve: true);
				tester.Explorer.CreateRPCClient().SendToAddress(newAddress.ScriptPubKey.GetDestinationAddress(tester.Network), Money.Coins(1.0m));
				utxo = tester.Client.GetUTXOs(alice, utxo);

				newAddress = tester.Client.GetUnused(alice, DerivationFeature.Direct, reserve: true);
				tester.Explorer.CreateRPCClient().SendToAddress(newAddress.ScriptPubKey.GetDestinationAddress(tester.Network), Money.Coins(1.0m));
				utxo = tester.Client.GetUTXOs(alice, utxo);

				utxo = tester.Client.GetUTXOs(alice, null);
				Assert.Equal(2, tester.Client.GetUTXOs(alice, null).GetUnspentCoins().Length);

				var ex = Assert.Throws<NBXplorerException>(() => tester.Client.LockUTXOs(alice, new LockUTXOsRequest()
				{
					Destination = bob.ToString(),
					Amount = Money.Coins(3),
					FeeRate = new FeeRate(Money.Satoshis(1), 1)
				}));
				Assert.Equal("not-enough-funds", ex.Error.Code);
				ex = Assert.Throws<NBXplorerException>(() => tester.Client.LockUTXOs(alice, new LockUTXOsRequest()
				{
					Destination = bob.ToString(),
					Amount = Money.Coins(3)
				}));
				Assert.Equal("fee-estimation-unavailable", ex.Error.Code);

				var locked = tester.Client.LockUTXOs(alice, new LockUTXOsRequest()
				{
					Destination = bob.ToString(),
					Amount = Money.Coins(0.4m),
					FeeRate = new FeeRate(Money.Satoshis(1), 1)
				});

				Assert.Single(locked.SpentCoins);
				Assert.Equal(Money.Coins(1.0m), locked.SpentCoins[0].Value);
				Assert.NotNull(locked.ChangeInformation);
				Assert.True(locked.ChangeInformation.Value.Almost(Money.Coins(0.6m), 0.001m));

				// Because one whole bitcoin is locked, we should not have enough fund
				ex = Assert.Throws<NBXplorerException>(() => tester.Client.LockUTXOs(alice, new LockUTXOsRequest()
				{
					Destination = bob.ToString(),
					Amount = Money.Coins(1.01m),
					FeeRate = new FeeRate(Money.Satoshis(1), 1)
				}));
				Assert.Equal("not-enough-funds", ex.Error.Code);

				// We have only 1 coins left
				var balance = tester.Client.GetBalance(alice);
				Assert.Equal(Money.Coins(1), balance.Spendable);
				// But really, we are left with our earlier change + this coin
				Assert.Equal(Money.Coins(1) + locked.ChangeInformation.Value, balance.Total);

				// Only the two transactions should show
				var transactions = tester.Client.GetTransactions(alice, null);
				Assert.Equal(2, transactions.UnconfirmedTransactions.Transactions.Count);

				// We should have only 1 UTXO left
				utxo = tester.Client.GetUTXOs(alice, null);
				Assert.Single(utxo.GetUnspentCoins());

				// Let's cancel
				Assert.True(tester.Client.UnlockUTXOs(locked.UnlockId));
				Assert.False(tester.Client.UnlockUTXOs(locked.UnlockId));

				// We should have 2 utxo now!
				utxo = tester.Client.GetUTXOs(alice, null);
				Assert.Equal(2, utxo.GetUnspentCoins().Length);
			}
		}

		[Fact]
		public void CanEasilySpendUTXOs()
		{
			using(var tester = ServerTester.Create())
			{
				var userExtKey = new ExtKey();
				var userDerivationScheme = tester.Client.Network.DerivationStrategyFactory.CreateDirectDerivationStrategy(userExtKey.Neuter(), new DerivationStrategyOptions()
				{
					// Use non-segwit
					Legacy = true
				});
				tester.Client.Track(userDerivationScheme);
				var utxos = tester.Client.GetUTXOs(userDerivationScheme, null, false);

				// Send 1 BTC
				var newAddress = tester.Client.GetUnused(userDerivationScheme, DerivationFeature.Direct);
				tester.Explorer.CreateRPCClient().SendToAddress(newAddress.ScriptPubKey.GetDestinationAddress(tester.Network), Money.Coins(1.0m));
				utxos = tester.Client.GetUTXOs(userDerivationScheme, utxos, true);

				// Send 1 more BTC
				newAddress = tester.Client.GetUnused(userDerivationScheme, DerivationFeature.Deposit);
				tester.Explorer.CreateRPCClient().SendToAddress(newAddress.ScriptPubKey.GetDestinationAddress(tester.Network), Money.Coins(1.0m));
				utxos = tester.Client.GetUTXOs(userDerivationScheme, utxos, true);

				utxos = tester.Client.GetUTXOs(userDerivationScheme, null, false);
				Assert.Equal(2, utxos.GetUnspentCoins().Length);
				for(int i = 0; i < 3; i++)
				{
					Money expected = i == 0 ? Money.Coins(2.0m) :
									 i == 1 ? Money.Coins(1.49977400m) : null;
					if(expected != null)
					{
						var balance = tester.Client.GetBalance(userDerivationScheme);
						Assert.Equal(expected, balance.Spendable);
						Assert.Equal(expected, balance.Total);
					}

					var changeAddress = tester.Client.GetUnused(userDerivationScheme, DerivationFeature.Change);
					var coins = utxos.GetUnspentCoins();
					var keys = utxos.GetKeys(userExtKey);
					TransactionBuilder builder = new TransactionBuilder();
					builder.AddCoins(coins);
					builder.AddKeys(keys);
					builder.Send(new Key(), Money.Coins(0.5m));
					builder.SetChange(changeAddress.ScriptPubKey);

					var fallbackFeeRate = new FeeRate(Money.Satoshis(100), 1);
					var feeRate = tester.Client.GetFeeRate(1, fallbackFeeRate).FeeRate;

					builder.SendEstimatedFees(feeRate);
					builder.SetConsensusFactory(tester.Network);
					var tx = builder.BuildTransaction(true);
					Assert.True(builder.Verify(tx));
					Assert.True(tester.Client.Broadcast(tx).Success);

					utxos = tester.Client.GetUTXOs(userDerivationScheme, utxos, true);
					utxos = tester.Client.GetUTXOs(userDerivationScheme, null, false);

					if(i == 0)
						Assert.Equal(2, utxos.GetUnspentCoins().Length);

					Assert.Contains(utxos.GetUnspentCoins(), u => u.ScriptPubKey == changeAddress.ScriptPubKey);
					Assert.Contains(utxos.Unconfirmed.UTXOs, u => u.ScriptPubKey == changeAddress.ScriptPubKey && u.Feature == DerivationFeature.Change);
				}
			}
		}

		[Fact]
		public void ShowRBFedTransaction()
		{
			using(var tester = ServerTester.Create())
			{
				var bob = tester.CreateDerivationStrategy();
				tester.Client.Track(bob);
				var utxo = tester.Client.GetUTXOs(bob, null, false); //Track things do not wait
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

				utxo = tester.Client.GetUTXOs(bob, utxo); //Wait tx received
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
				var replacement = tester.RPC.SignRawTransaction(tx);

				tester.RPC.SendRawTransaction(replacement);

				var prevUtxo = utxo;
				utxo = tester.Client.GetUTXOs(bob, prevUtxo); //Wait tx received
				Assert.Null(utxo.Unconfirmed.KnownBookmark);
				Assert.Equal(replacement.GetHash(), utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				Assert.Single(utxo.Unconfirmed.UTXOs);

				var txs = tester.Client.GetTransactions(bob, null);
				Assert.Single(txs.UnconfirmedTransactions.Transactions);
				Assert.Equal(replacement.GetHash(), txs.UnconfirmedTransactions.Transactions[0].TransactionId);
				Assert.Single(txs.ReplacedTransactions.Transactions);
				Assert.Equal(tx1, txs.ReplacedTransactions.Transactions[0].TransactionId);
			}
		}

		[Fact]
		public void CanGetUnusedAddresses()
		{
			using(var tester = ServerTester.Create())
			{
				var bob = tester.CreateDerivationStrategy();
				var utxo = tester.Client.GetUTXOs(bob, null, false); //Track things do not wait

				var a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);
				Assert.Null(a1);
				tester.Client.Track(bob);
				a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);
				Assert.NotNull(a1);
				Assert.NotNull(a1.Address);

				var a12 = tester.Client.GetKeyInformationFromKeyPath(bob, a1.KeyPath);
				Assert.Equal(a12.Address, a1.Address);

				Assert.Equal(a1.ScriptPubKey, tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0).ScriptPubKey);
				Assert.Equal(a1.ScriptPubKey, bob.Derive(new KeyPath("0/0")).ScriptPubKey);

				var a2 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, skip: 1);
				Assert.Equal(a2.ScriptPubKey, bob.Derive(new KeyPath("0/1")).ScriptPubKey);

				var a3 = tester.Client.GetUnused(bob, DerivationFeature.Change, skip: 0);
				Assert.Equal(a3.ScriptPubKey, bob.Derive(new KeyPath("1/0")).ScriptPubKey);

				var a4 = tester.Client.GetUnused(bob, DerivationFeature.Direct, skip: 1);
				Assert.Equal(a4.ScriptPubKey, bob.Derive(new KeyPath("1")).ScriptPubKey);

				Assert.Null(tester.Client.GetUnused(bob, DerivationFeature.Change, skip: 30));

				a3 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, skip: 2);
				Assert.Equal(new KeyPath("0/2"), a3.KeyPath);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
				//   0/0 and 0/2 used
				tester.SendToAddressAsync(a1.ScriptPubKey.GetDestinationAddress(tester.Network), Money.Coins(1.0m));
				utxo = tester.Client.GetUTXOs(bob, utxo); //Wait tx received

				tester.SendToAddressAsync(a3.ScriptPubKey.GetDestinationAddress(tester.Network), Money.Coins(1.0m));
				utxo = tester.Client.GetUTXOs(bob, utxo); //Wait tx received

				tester.SendToAddressAsync(a4.ScriptPubKey.GetDestinationAddress(tester.Network), Money.Coins(1.0m));
				utxo = tester.Client.GetUTXOs(bob, utxo); //Wait tx received
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
				a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);
				Assert.Equal(a1.ScriptPubKey, bob.Derive(new KeyPath("0/1")).ScriptPubKey);
				a2 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, skip: 1);
				Assert.Equal(a2.ScriptPubKey, bob.Derive(new KeyPath("0/3")).ScriptPubKey);

				a4 = tester.Client.GetUnused(bob, DerivationFeature.Direct, skip: 1);
				Assert.Equal(a4.ScriptPubKey, bob.Derive(new KeyPath("2")).ScriptPubKey);

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
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), true);
				tester.Client.Track(pubkey);
				using(var connected = tester.Client.CreateNotificationSession())
				{
					connected.ListenNewBlock();
					var expectedBlockId = tester.Explorer.CreateRPCClient().Generate(1)[0];
					var blockEvent = (Models.NewBlockEvent)connected.NextEvent(Cancel);
					Assert.Equal(expectedBlockId, blockEvent.Hash);
					Assert.NotEqual(0, blockEvent.Height);

					connected.ListenDerivationSchemes(new[] { pubkey });
					tester.Explorer.CreateRPCClient().SendToAddress(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}

				using(var connected = tester.Client.CreateNotificationSession())
				{
					connected.ListenAllDerivationSchemes();
					tester.Explorer.CreateRPCClient().SendToAddress(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}
			}
		}

		[Fact]
		public void CanUseWebSockets2()
		{
			using(var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), false);

				var pubkey2 = tester.CreateDerivationStrategy(key.Neuter(), true);

				tester.Client.Track(pubkey);
				tester.Client.Track(pubkey2);
				using(var connected = tester.Client.CreateNotificationSession())
				{
					connected.ListenAllDerivationSchemes();
					tester.Explorer.CreateRPCClient().SendCommand(RPCOperations.sendmany, "",
						JObject.Parse($"{{ \"{tester.AddressOf(pubkey, "0/1")}\": \"0.9\", \"{tester.AddressOf(pubkey, "1/1")}\": \"0.5\"," +
									  $"\"{tester.AddressOf(pubkey2, "0/2")}\": \"0.9\", \"{tester.AddressOf(pubkey2, "1/2")}\": \"0.5\" }}"));

					var schemes = new[] { pubkey.ToString(), pubkey2.ToString() }.ToList();

					int expectedOutput = tester.SupportSegwit() ? 2 : 4; // if does not support segwit pubkey == pubkey2
					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(expectedOutput, txEvent.Outputs.Count);
					Assert.Contains(txEvent.DerivationStrategy.ToString(), schemes);
					schemes.Remove(txEvent.DerivationStrategy.ToString());

					if(!tester.SupportSegwit())
						return;

					txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(2, txEvent.Outputs.Count);
					Assert.Contains(txEvent.DerivationStrategy.ToString(), new[] { pubkey.ToString(), pubkey2.ToString() });
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

				var bobPubKey = tester.CreateDerivationStrategy(bob.Neuter());
				var alicePubKey = tester.CreateDerivationStrategy(alice.Neuter());

				tester.Client.Track(alicePubKey);
				var utxoAlice = tester.Client.GetUTXOs(alicePubKey, Bookmark.Start, Bookmark.Start, true); //Track things do not wait
				tester.Client.Track(bobPubKey);
				var utxoBob = tester.Client.GetUTXOs(bobPubKey, null, false); //Track things do not wait
				Assert.NotNull(utxoAlice.Confirmed.KnownBookmark);
				Assert.NotNull(utxoAlice.Unconfirmed.KnownBookmark);

				var id = tester.SendToAddress(tester.AddressOf(alice, "0/1"), Money.Coins(1.0m));
				id = tester.SendToAddress(tester.AddressOf(bob, "0/2"), Money.Coins(0.1m));
				utxoAlice = tester.Client.GetUTXOs(alicePubKey, utxoAlice);
				utxoBob = tester.Client.GetUTXOs(bobPubKey, utxoBob);
				Assert.NotNull(utxoAlice.Unconfirmed.KnownBookmark);

				tester.RPC.EnsureGenerate(1);

				utxoAlice = tester.Client.GetUTXOs(alicePubKey, utxoAlice);
				utxoBob = tester.Client.GetUTXOs(bobPubKey, utxoBob);
				Assert.NotNull(utxoAlice.Confirmed.KnownBookmark);

				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(alice, "0/1"));
				tester.SendToAddress(tester.AddressOf(bob, "0/3"), Money.Coins(0.6m));

				utxoAlice = tester.Client.GetUTXOs(alicePubKey, utxoAlice);
				utxoBob = tester.Client.GetUTXOs(bobPubKey, utxoBob);
				Assert.NotNull(utxoAlice.Unconfirmed.KnownBookmark);

				utxoAlice = tester.Client.GetUTXOs(alicePubKey, utxoAlice, false);
				Assert.NotNull(utxoAlice.Unconfirmed.KnownBookmark);

				tester.RPC.EnsureGenerate(1);

				utxoAlice = tester.Client.GetUTXOs(alicePubKey, utxoAlice);
				utxoBob = tester.Client.GetUTXOs(bobPubKey, utxoBob);

				Assert.NotNull(utxoAlice.Confirmed.KnownBookmark);
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
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);
				tester.Client.GetUTXOs(pubkey, null, false); //Track things do not wait
				var events = tester.Client.CreateNotificationSession();
				events.ListenDerivationSchemes(new[] { pubkey });

				var id = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				id = tester.SendToAddress(tester.AddressOf(key, "0/1"), Money.Coins(1.1m));
				id = tester.SendToAddress(tester.AddressOf(key, "0/2"), Money.Coins(1.2m));
				id = tester.SendToAddress(tester.AddressOf(key, "0"), Money.Coins(1.2m));
				id = tester.SendToAddress(tester.AddressOf(key, "1"), Money.Coins(1.2m));

				events.NextEvent(Timeout);
				events.NextEvent(Timeout);
				events.NextEvent(Timeout);
				events.NextEvent(Timeout);
				events.NextEvent(Timeout);
				var utxo = tester.Client.GetUTXOs(pubkey, null);

				tester.RPC.EnsureGenerate(1);

				var prev = utxo;
				utxo = tester.Client.GetUTXOs(pubkey, prev);
				Assert.True(utxo.HasChanges);
				Assert.Equal(5, utxo.Confirmed.UTXOs.Count);
				utxo = tester.Client.GetUTXOs(pubkey, utxo, false);
				Assert.False(utxo.HasChanges);
			}
		}

		[Fact]
		public void CanTrackSeveralTransactions()
		{
			using(var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);
				var utxo = tester.Client.GetUTXOs(pubkey, null, false); //Track things do not wait

				var addresses = new HashSet<Script>();
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/0"));
				var id = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				addresses.Add(tester.AddressOf(key, "0/0").ScriptPubKey);

				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.True(utxo.HasChanges);

				var coins = Money.Coins(1.0m);
				int i = 0;
				for(i = 0; i < 20; i++)
				{
					LockTestCoins(tester.RPC, addresses);
					var spendable = tester.RPC.ListUnspent(0, 0);
					coins = coins - Money.Coins(0.001m);
					var destination = tester.AddressOf(key, $"0/{i + 1}");

					tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, $"0/{i + 1}"));
					tester.SendToAddress(destination, coins);
					addresses.Add(destination.ScriptPubKey);
				}

				while(true)
				{
					utxo = tester.Client.GetUTXOs(pubkey, utxo, true, Timeout);
					if(!utxo.HasChanges)
						continue;
					Assert.NotNull(utxo.Confirmed.KnownBookmark);
					Assert.True(utxo.Unconfirmed.HasChanges);
					Assert.Single(utxo.Unconfirmed.UTXOs);
					if(new KeyPath($"0/{i}").Equals(utxo.Unconfirmed.UTXOs[0].KeyPath))
						break;
				}

				tester.RPC.EnsureGenerate(1);

				utxo = tester.Client.GetUTXOs(pubkey, utxo);
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
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);
				var utxo = tester.Client.GetUTXOs(pubkey, null, false); //Track things do not wait
				var tx1 = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.NotNull(utxo.Confirmed.KnownBookmark);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(tx1, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);


				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/0"));
				var tx2 = tester.SendToAddress(tester.AddressOf(key, "1/0"), Money.Coins(0.6m));

				var prevUtxo = utxo;
				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.NotNull(utxo.Unconfirmed.KnownBookmark);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(tx2, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash); //got the 0.6m
				Assert.Equal(Money.Coins(0.6m), utxo.Unconfirmed.UTXOs[0].Value); //got the 0.6m

				Assert.Single(utxo.Unconfirmed.SpentOutpoints);
				Assert.Equal(tx1, utxo.Unconfirmed.SpentOutpoints[0].Hash); //previous coin is spent

				utxo = tester.Client.GetUTXOs(pubkey, prevUtxo.Confirmed.Bookmark, null);
				Assert.Null(utxo.Unconfirmed.KnownBookmark);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Empty(utxo.Unconfirmed.SpentOutpoints); //should be skipped as the unconf coin were not known

				tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(0.15m));

				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.IsType<Coin>(utxo.Unconfirmed.UTXOs[0].AsCoin(pubkey));
				Assert.Equal(Money.Coins(0.15m), utxo.Unconfirmed.UTXOs[0].Value);
				Assert.Empty(utxo.Unconfirmed.SpentOutpoints);

				utxo = tester.Client.GetUTXOs(pubkey, null);
				Assert.Equal(2, utxo.Unconfirmed.UTXOs.Count); //Should have 0.15 and 0.6
				Assert.Equal(Money.Coins(0.75m), utxo.Unconfirmed.UTXOs.Select(c => c.Value).Sum());
				Assert.Empty(utxo.Unconfirmed.SpentOutpoints);
			}
		}

		[Fact]
		public void CanReserveAddress()
		{
			using(var tester = ServerTester.Create(opts: new ServerTester.Options()
			{
				MinGap = 10,
				MaxGap = 20
			}))
			{
				//WaitServerStarted not needed, just a sanity check
				var bob = tester.CreateDerivationStrategy();
				tester.Client.WaitServerStarted();
				tester.Client.Track(bob);
				var utxo = tester.Client.GetUTXOs(bob, null, false); //Track things do not wait

				var tasks = new List<Task<KeyPathInformation>>();
				for(int i = 0; i < 10; i++)
				{
					tasks.Add(tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit, reserve: true));
				}
				Task.WaitAll(tasks.ToArray());

				var paths = tasks.Select(t => t.Result).ToDictionary(c => c.KeyPath);
				Assert.Equal(9U, paths.Select(p => p.Key.Indexes.Last()).Max());

				tester.Client.CancelReservation(bob, new[] { new KeyPath("0/0") });
				Assert.Equal(new KeyPath("0/0"), tester.Client.GetUnused(bob, DerivationFeature.Deposit).KeyPath);
			}
		}

		[Fact]
		public void GenerateMultiMultisigDerivationSchemes()
		{
			var network = Network.TestNet;
			var factory = new DerivationStrategyFactory(network);
			Random rand = new Random();
			for(int i = 0; i < 3; i++)
			{
				var req1 = rand.Next(1, 3);
				var keys1Count = rand.Next(req1, 3);
				var keys1 = Enumerable.Range(0, keys1Count).Select(_ => new ExtKey().Neuter().GetWif(network)).ToArray();

				var req2 = rand.Next(1, 7);
				var keys2Count = rand.Next(req2, 7);
				var hs = new ExtKey().Neuter().GetWif(network);

				StringBuilder scheme = new StringBuilder();
				scheme.Append($"{req1}-of-");
				scheme.Append(string.Join("-", keys1.OfType<object>().ToArray()));
				scheme.Append("-and-");
				scheme.Append(hs);
				scheme.Append("-[p2sh]");
				var derivationScheme = factory.Parse(scheme.ToString());

				Logs.Tester.LogInformation("scheme: " + scheme.ToString());
				// Derive 0/0
				var derivation = derivationScheme.GetLineFor(DerivationFeature.Deposit).Derive(new KeyPath(0));

				Logs.Tester.LogInformation("segwit redeem: " + derivation.Redeem.ToString());
				Logs.Tester.LogInformation("segwit redeem (hex): " + derivation.Redeem.ToHex());
				var redeem = derivation.Redeem;

				var p2psh = new Coin(new OutPoint(), new TxOut() { ScriptPubKey = derivation.ScriptPubKey }).ToScriptCoin(derivation.Redeem).GetP2SHRedeem();

				Logs.Tester.LogInformation("p2sh redeem: " + p2psh.ToString());
				Logs.Tester.LogInformation("p2sh redeem (hex): " + p2psh.ToHex());

				Logs.Tester.LogInformation("scriptPubKey: " + derivation.ScriptPubKey.ToString());
				Logs.Tester.LogInformation("scriptPubKey (hex): " + derivation.ScriptPubKey.ToHex());
				var scriptPubKey = derivation.ScriptPubKey;
				Logs.Tester.LogInformation("---------");
			}

			// Uncomment this to see the logs in test output
			// throw new Exception();
		}

		[Fact]
		public void CanParseDerivationScheme()
		{
			var network = Network.Main;
			var factory = new DerivationStrategy.DerivationStrategyFactory(network);
			var tata = new BitcoinExtPubKey("xpub661MyMwAqRbcFiadHioAunPTeic3C17HKPABCBvURz3W2ivn63jzEYYXWpDePLGncjLuRvQKx7jrKweSkoEvgQTvAo5zw4z8HPGC8Y4E4Wr", network);
			var toto = new BitcoinExtPubKey("xpub661MyMwAqRbcFqyJE6zy5jMF7bjUtvNHgHJPbENEZtEQKRrukKWJP5xLMKntBaNya7CLMLL6u1KEk8GnrEv8pur5DFSgEMf1hRGjsJrcQKS", network);

			var direct = (DirectDerivationStrategy)factory.Parse($"{toto}-[legacy]");
			Assert.Equal($"{toto}-[legacy]", direct.ToString());
			var generated = Generate(direct);
			Assert.Equal(toto.ExtPubKey.Derive(new KeyPath("0/1")).PubKey.Hash.ScriptPubKey, generated.ScriptPubKey);
			Assert.Null(generated.Redeem);

			var p2wpkh = (DirectDerivationStrategy)factory.Parse($"{toto}");
			Assert.Equal($"{toto}", p2wpkh.ToString());
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
			Assert.Equal($"{toto}-[p2sh]", p2shp2wpkh.ToString());
			Assert.NotNull(generated.Redeem);
			Assert.Equal(toto.ExtPubKey.Derive(new KeyPath("0/1")).PubKey.WitHash.ScriptPubKey.Hash.ScriptPubKey, generated.ScriptPubKey);
			Assert.Equal(toto.ExtPubKey.Derive(new KeyPath("0/1")).PubKey.WitHash.ScriptPubKey, generated.Redeem);

			var multiSig = (P2SHDerivationStrategy)factory.Parse($"2-of-{toto}-{tata}-[legacy]");
			generated = Generate(multiSig);
			Assert.Equal(new Script("2 025ca59b2007a67f24fdd26acefbe8feb5e8849c207d504b16d4801a8290fe9409 03d15f88de692693e0c25cec27b68da49ae4c29805efbe08154c4acfdf951ccb54 2 OP_CHECKMULTISIG"), generated.Redeem);
			multiSig = (P2SHDerivationStrategy)factory.Parse($"2-of-{toto}-{tata}-[legacy]-[keeporder]");
			Assert.Equal($"2-of-{toto}-{tata}-[legacy]-[keeporder]", multiSig.ToString());
			generated = Generate(multiSig);
			Assert.Equal(new Script("2 03d15f88de692693e0c25cec27b68da49ae4c29805efbe08154c4acfdf951ccb54 025ca59b2007a67f24fdd26acefbe8feb5e8849c207d504b16d4801a8290fe9409 2 OP_CHECKMULTISIG"), generated.Redeem);

			var multiP2SH = (P2WSHDerivationStrategy)factory.Parse($"2-of-{toto}-{tata}");
			Assert.Equal($"2-of-{toto}-{tata}", multiP2SH.ToString());
			generated = Generate(multiP2SH);
			Assert.IsType<WitScriptId>(generated.ScriptPubKey.GetDestination());
			Assert.NotNull(PayToMultiSigTemplate.Instance.ExtractScriptPubKeyParameters(generated.Redeem));

			var multiP2WSHP2SH = (P2SHDerivationStrategy)factory.Parse($"2-of-{toto}-{tata}-[p2sh]");
			Assert.Equal($"2-of-{toto}-{tata}-[p2sh]", multiP2WSHP2SH.ToString());
			generated = Generate(multiP2WSHP2SH);
			Assert.IsType<ScriptId>(generated.ScriptPubKey.GetDestination());
			Assert.NotNull(PayToMultiSigTemplate.Instance.ExtractScriptPubKeyParameters(generated.Redeem));

			var multimultiP2WSHP2SH = (P2SHDerivationStrategy)factory.Parse($"2-of-{toto}-{tata}-and-1-of-{tata}-[p2sh]");
			var multimulti = Assert.IsType<MultisigAndMultisigDerivationStrategy>(Assert.IsType<P2WSHDerivationStrategy>(multimultiP2WSHP2SH.Inner).Inner);
			Assert.False(multimulti.IsLegacy);
			Assert.True(multimulti.LexicographicOrder);
			Assert.Equal($"2-of-{toto}-{tata}-and-1-of-{tata}-[p2sh]", multimultiP2WSHP2SH.ToString());
			Assert.Equal($"2-of-{toto}-{tata}-and-1-of-{tata}", multimulti.ToString());

			var multioneP2WSHP2SH = (P2SHDerivationStrategy)factory.Parse($"2-of-{toto}-{tata}-and-{tata}-[p2sh]");
			var multione = Assert.IsType<MultisigPlusOneDerivationStrategy>(Assert.IsType<P2WSHDerivationStrategy>(multioneP2WSHP2SH.Inner).Inner);
			Assert.Equal($"2-of-{toto}-{tata}-and-{tata}", multione.ToString());
			multione.GetLineFor(new KeyPath(0));
		}

		private static Derivation Generate(DerivationStrategyBase strategy)
		{
			var derivation = strategy.GetLineFor(DerivationFeature.Deposit).Derive(1);
			var derivation2 = strategy.Derive(DerivationStrategyBase.GetKeyPath(DerivationFeature.Deposit).Derive(1));
			Assert.Equal(derivation.Redeem, derivation2.Redeem);
			return derivation;
		}

		[Fact]
		public void CanGetStatus()
		{
			using(var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted(Timeout);
				var status = tester.Client.GetStatus();
				Assert.NotNull(status.BitcoinStatus);
				Assert.True(status.IsFullySynched);
				Assert.Equal(status.BitcoinStatus.Blocks, status.BitcoinStatus.Headers);
				Assert.Equal(status.BitcoinStatus.Blocks, status.ChainHeight);
				Assert.Equal(1.0, status.BitcoinStatus.VerificationProgress);
				Assert.NotNull(status.Version);
				Assert.Equal(tester.CryptoCode, status.CryptoCode);
				Assert.Equal(NetworkType.Regtest, status.NetworkType);
				Assert.Equal(tester.CryptoCode, status.SupportedCryptoCodes[0]);
				Assert.Single(status.SupportedCryptoCodes);
			}
		}

		public CancellationToken Timeout => new CancellationTokenSource(10000).Token;


		[Fact]
		public void CanGetTransactionsOfDerivation()
		{
			using(var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted(Timeout);
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());

				tester.Client.Track(pubkey);
				var utxo = tester.Client.GetUTXOs(pubkey, null, false); //Track things do not wait

				var txId = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				var result = tester.Client.GetTransactions(pubkey, new[] { Bookmark.Start }, new[] { Bookmark.Start }, new[] { Bookmark.Start });
				Assert.True(result.HasChanges());
				Assert.Single(result.UnconfirmedTransactions.Transactions);
				var height = result.Height;
				var timestampUnconf = result.UnconfirmedTransactions.Transactions[0].Timestamp;
				Assert.Null(result.UnconfirmedTransactions.Transactions[0].BlockHash);
				Assert.Null(result.UnconfirmedTransactions.Transactions[0].Height);
				Assert.Equal(0, result.UnconfirmedTransactions.Transactions[0].Confirmations);
				Assert.Equal(result.UnconfirmedTransactions.Transactions[0].Transaction.GetHash(), result.UnconfirmedTransactions.Transactions[0].TransactionId);
				Assert.Equal(Money.Coins(1.0m), result.UnconfirmedTransactions.Transactions[0].BalanceChange);

				tester.Client.IncludeTransaction = false;
				result = tester.Client.GetTransactions(pubkey, new[] { Bookmark.Start }, new[] { Bookmark.Start }, new[] { Bookmark.Start });
				Assert.Null(result.UnconfirmedTransactions.Transactions[0].Transaction);

				result = tester.Client.GetTransactions(pubkey, result, false);
				Assert.False(result.HasChanges());

				tester.RPC.EnsureGenerate(1);
				result = tester.Client.GetTransactions(pubkey, result);

				Assert.True(result.HasChanges());
				Assert.Null(result.UnconfirmedTransactions.KnownBookmark);

				var gotConf = result.ConfirmedTransactions.Bookmark;

				var txId2 = tester.SendToAddress(tester.AddressOf(key, "0"), Money.Coins(1.0m));
				result = tester.Client.GetTransactions(pubkey, result);
				Assert.True(result.HasChanges());
				Assert.Equal(gotConf, result.ConfirmedTransactions.KnownBookmark);
				Assert.Single(result.UnconfirmedTransactions.Transactions);
				Assert.Equal(txId2, result.UnconfirmedTransactions.Transactions[0].TransactionId);

				result = tester.Client.GetTransactions(pubkey, null, null, null, false);
				Assert.True(result.HasChanges());
				Assert.Single(result.ConfirmedTransactions.Transactions);
				Assert.Single(result.UnconfirmedTransactions.Transactions);
				Assert.Equal(txId, result.ConfirmedTransactions.Transactions[0].TransactionId);
				Assert.Equal(timestampUnconf, result.ConfirmedTransactions.Transactions[0].Timestamp);
				Assert.Equal(txId2, result.UnconfirmedTransactions.Transactions[0].TransactionId);

				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/0"));
				var txId3 = tester.SendToAddress(tester.AddressOf(key, "0/1"), Money.Coins(0.2m));
				result = tester.Client.GetTransactions(pubkey, result);
				Assert.Equal(Money.Coins(-0.8m), result.UnconfirmedTransactions.Transactions[0].BalanceChange);
			}
		}

		[Fact]
		public void CanTrack5()
		{
			using(var tester = ServerTester.Create())
			{
				//WaitServerStarted not needed, just a sanity check
				tester.Client.WaitServerStarted(Timeout);
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());

				tester.Client.Track(pubkey);
				var utxo = tester.Client.GetUTXOs(pubkey, null, false); //Track things do not wait

				// We receive money
				var fundingTx = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				tester.RPC.EnsureGenerate(1);
				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.Single(utxo.Confirmed.UTXOs);

				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/0"));
				var spendingTx = tester.SendToAddress(new Key().PubKey.Hash.GetAddress(tester.Network), Money.Coins(0.2m));

				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.False(utxo.Confirmed.HasChanges); // No change here
				Assert.True(utxo.Unconfirmed.HasChanges);
				Assert.Single(utxo.Unconfirmed.SpentOutpoints);
				Assert.Equal(fundingTx, utxo.Unconfirmed.SpentOutpoints[0].Hash);

				utxo = tester.Client.GetUTXOs(pubkey, null, false);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Single(utxo.Unconfirmed.SpentOutpoints);
				Assert.Equal(fundingTx, utxo.Unconfirmed.SpentOutpoints[0].Hash);
			}
		}

		[Fact]
		public void CanTrack()
		{
			using(var tester = ServerTester.Create())
			{
				//WaitServerStarted not needed, just a sanity check
				tester.Client.WaitServerStarted(Timeout);
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());

				tester.Client.Track(pubkey);
				var utxo = tester.Client.GetUTXOs(pubkey, null, false); //Track things do not wait
				var gettingUTXO = tester.Client.GetUTXOsAsync(pubkey, utxo);
				var txId = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				utxo = gettingUTXO.GetAwaiter().GetResult();
				Assert.Equal(tester.Network.Consensus.CoinbaseMaturity + 3, utxo.CurrentHeight);

				Assert.NotNull(utxo.Confirmed.KnownBookmark);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(txId, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				var unconfTimestamp = utxo.Unconfirmed.UTXOs[0].Timestamp;
				Assert.Equal(0, utxo.Unconfirmed.UTXOs[0].Confirmations);
				Assert.Empty(utxo.Confirmed.UTXOs);
				Assert.Equal(Bookmark.Start, utxo.Confirmed.Bookmark);
				Assert.NotEqual(Bookmark.Start, utxo.Unconfirmed.Bookmark);
				var tx = tester.Client.GetTransaction(utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				Assert.NotNull(tx);
				Assert.Equal(0, tx.Confirmations);
				Assert.Null(tx.BlockId);
				Assert.Equal(utxo.Unconfirmed.UTXOs[0].Outpoint.Hash, tx.Transaction.GetHash());
				Assert.Equal(unconfTimestamp, tx.Timestamp);

				tester.RPC.EnsureGenerate(1);
				var prevUtxo = utxo;
				utxo = tester.Client.GetUTXOs(pubkey, prevUtxo);
				Assert.Null(utxo.Unconfirmed.KnownBookmark);
				Assert.Empty(utxo.Unconfirmed.UTXOs);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(txId, utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.Equal(1, utxo.Confirmed.UTXOs[0].Confirmations);
				Assert.Equal(unconfTimestamp, utxo.Confirmed.UTXOs[0].Timestamp);
				Assert.NotEqual(Bookmark.Start, utxo.Confirmed.Bookmark);
				var prevConfHash = utxo.Confirmed.Bookmark;

				txId = tester.SendToAddress(tester.AddressOf(key, "0/1"), Money.Coins(1.0m));
				var txId1 = txId;

				prevUtxo = utxo;
				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.Empty(utxo.Confirmed.UTXOs);
				Assert.NotNull(utxo.Confirmed.KnownBookmark);
				Assert.True(utxo.HasChanges);
				Assert.NotNull(utxo.Unconfirmed.KnownBookmark);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(txId, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				utxo = tester.Client.GetUTXOs(pubkey, null, false);

				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(new KeyPath("0/1"), utxo.Unconfirmed.UTXOs[0].KeyPath);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(new KeyPath("0/0"), utxo.Confirmed.UTXOs[0].KeyPath);
				Assert.Equal(prevConfHash, utxo.Confirmed.Bookmark);

				utxo = tester.Client.GetUTXOs(pubkey, utxo.Confirmed.Bookmark, null, false);
				Assert.NotNull(utxo.Confirmed.KnownBookmark);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Empty(utxo.Confirmed.UTXOs);

				utxo = tester.Client.GetUTXOs(pubkey, null, utxo.Unconfirmed.Bookmark, false);
				Assert.Null(utxo.Confirmed.KnownBookmark);
				Assert.Empty(utxo.Unconfirmed.UTXOs);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(1, utxo.Confirmed.UTXOs[0].Confirmations);

				Assert.Null(tester.Client.GetTransaction(uint256.One));
				tx = tester.Client.GetTransaction(utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.NotNull(tx);
				Assert.Equal(unconfTimestamp, tx.Timestamp);
				Assert.Equal(1, tx.Confirmations);
				Assert.NotNull(tx.BlockId);
				Assert.Equal(utxo.Confirmed.UTXOs[0].Outpoint.Hash, tx.Transaction.GetHash());
				tester.RPC.EnsureGenerate(1);

				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(new KeyPath("0/1"), utxo.Confirmed.UTXOs[0].KeyPath);

				tx = tester.Client.GetTransaction(tx.Transaction.GetHash());
				Assert.Equal(2, tx.Confirmations);
				Assert.NotNull(tx.BlockId);

				var outpoint01 = utxo.Confirmed.UTXOs[0].Outpoint;

				txId = tester.SendToAddress(tester.AddressOf(key, "0/2"), Money.Coins(1.0m));
				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Empty(utxo.Confirmed.UTXOs);
				Assert.Equal(new KeyPath("0/2"), utxo.Unconfirmed.UTXOs[0].KeyPath);
				tester.RPC.EnsureGenerate(1);

				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(new KeyPath("0/2"), utxo.Confirmed.UTXOs[0].KeyPath);

				tx = tester.Client.GetTransaction(tx.Transaction.GetHash());
				Assert.Equal(3, tx.Confirmations);
				Assert.NotNull(tx.BlockId);

				utxo = tester.Client.GetUTXOs(pubkey, utxo, false);
				Assert.True(!utxo.HasChanges);

				var before01Spend = utxo.Confirmed.Bookmark;

				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/1"));
				txId = tester.SendToAddress(tester.AddressOf(key, "0/3"), Money.Coins(0.5m));

				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(new KeyPath("0/3"), utxo.Unconfirmed.UTXOs[0].KeyPath);
				Assert.Single(utxo.Unconfirmed.SpentOutpoints); // "0/1" should be spent
				Assert.Equal(txId1, utxo.Unconfirmed.SpentOutpoints[0].Hash); // "0/1" should be spent

				utxo = tester.Client.GetUTXOs(pubkey, utxo, false);
				Assert.False(utxo.HasChanges);
				tester.RPC.EnsureGenerate(1);

				utxo = tester.Client.GetUTXOs(pubkey, before01Spend, utxo.Unconfirmed.Bookmark);
				Assert.True(utxo.Unconfirmed.HasChanges);

				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(new KeyPath("0/3"), utxo.Confirmed.UTXOs[0].KeyPath);
				Assert.Single(utxo.Confirmed.SpentOutpoints);
				Assert.Equal(outpoint01, utxo.Confirmed.SpentOutpoints[0]);

				utxo = tester.Client.GetUTXOs(pubkey, utxo, false);
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
#pragma warning disable CS0618 // Type or member is obsolete
			var tx1 = new Transaction() { Outputs = { new TxOut(Money.Zero, new Key()) } };
			var tx2 = new Transaction() { Inputs = { new TxIn(new OutPoint(tx1, 0)) } };
			var tx3 = new Transaction() { Inputs = { new TxIn(new OutPoint(tx2, 0)) } };
#pragma warning restore CS0618 // Type or member is obsolete
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
				var tx = tester.Network.Consensus.ConsensusFactory.CreateTransaction();
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

				var oneSatPerByte = new FeeRate(Money.Satoshis(1), 1);
				tester.ExplorerConfiguration.ChainConfigurations[0].FallbackFeeRate = oneSatPerByte;

				var fee = tester.Client.GetFeeRate(5);
				Assert.Equal(oneSatPerByte, fee.FeeRate);
			}
		}

		[Fact]
		public void CanReserveDirectAddress()
		{
			using(var tester = ServerTester.Create(opts: new ServerTester.Options()
			{
				MinGap = 10,
				MaxGap = 20
			}))
			{
				//WaitServerStarted not needed, just a sanity check
				var bob = tester.CreateDerivationStrategy();
				tester.Client.WaitServerStarted();
				tester.Client.Track(bob);
				var utxo = tester.Client.GetUTXOs(bob, null, false); //Track things do not wait

				var tasks = new List<Task<KeyPathInformation>>();
				for(int i = 0; i < 10; i++)
				{
					tasks.Add(tester.Client.GetUnusedAsync(bob, DerivationFeature.Direct, reserve: true));
				}
				Task.WaitAll(tasks.ToArray());

				var paths = tasks.Select(t => t.Result).ToDictionary(c => c.KeyPath);
				Assert.Equal(9U, paths.Select(p => p.Key.Indexes.Last()).Max());

				tester.Client.CancelReservation(bob, new[] { new KeyPath("0") });
				var path = tester.Client.GetUnused(bob, DerivationFeature.Direct).KeyPath;
				Assert.Equal(new KeyPath("0"), path);
			}
		}

		[Fact]
		public void CanGetKeyInformations()
		{
			using(var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);

				KeyPathInformation[] keyinfos;
				var script = pubkey.Derive(new KeyPath("0/0")).ScriptPubKey;

				keyinfos = tester.Client.GetKeyInformations(script);
				Assert.NotNull(keyinfos);
				Assert.True(keyinfos.Length > 0);
				foreach(var k in keyinfos)
				{
					Assert.Equal(pubkey, k.DerivationStrategy);
					Assert.Equal(script, k.ScriptPubKey);
					Assert.Equal(new KeyPath("0/0"), k.KeyPath);
					Assert.Equal(DerivationFeature.Deposit, k.Feature);
				}
			}
		}

		[Fact]
		public void CanTrackDirect()
		{
			using(var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);
				var utxo = tester.Client.GetUTXOs(pubkey, null, false); //Track things do not wait
				var tx1 = tester.SendToAddress(tester.AddressOf(key, "0"), Money.Coins(1.0m));
				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.NotNull(utxo.Confirmed.KnownBookmark);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(tx1, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);


				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0"));
				var tx2 = tester.SendToAddress(tester.AddressOf(key, "1"), Money.Coins(0.6m));

				var prevUtxo = utxo;
				var before = utxo;
				utxo = before;
				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.NotNull(utxo.Unconfirmed.KnownBookmark);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(tx2, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash); //got the 0.6m
				Assert.Equal(Money.Coins(0.6m), utxo.Unconfirmed.UTXOs[0].Value); //got the 0.6m

				Assert.Single(utxo.Unconfirmed.SpentOutpoints);
				Assert.Equal(tx1, utxo.Unconfirmed.SpentOutpoints[0].Hash); //previous coin is spent

				utxo = tester.Client.GetUTXOs(pubkey, prevUtxo.Confirmed.Bookmark, null);
				Assert.Null(utxo.Unconfirmed.KnownBookmark);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Empty(utxo.Unconfirmed.SpentOutpoints); //should be skipped as the unconf coin were not known

				tester.SendToAddress(tester.AddressOf(key, "0"), Money.Coins(0.15m));

				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.IsType<Coin>(utxo.Unconfirmed.UTXOs[0].AsCoin(pubkey));
				Assert.Equal(Money.Coins(0.15m), utxo.Unconfirmed.UTXOs[0].Value);
				Assert.Empty(utxo.Unconfirmed.SpentOutpoints);

				utxo = tester.Client.GetUTXOs(pubkey, null);
				Assert.Equal(2, utxo.Unconfirmed.UTXOs.Count); //Should have 0.15 and 0.6
				Assert.Equal(Money.Coins(0.75m), utxo.Unconfirmed.UTXOs.Select(c => c.Value).Sum());
				Assert.Empty(utxo.Unconfirmed.SpentOutpoints);
			}
		}


	}
}
