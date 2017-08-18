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

namespace NBXplorer.Tests
{
	public class UnitTest1
	{
		public UnitTest1(ITestOutputHelper output)
		{
			Logs.Configure(new TestOutputHelperFactory(output));
		}

		[Fact]
		public void RepositoryCanTrackAddresses()
		{
			using(var tester = RepositoryTester.Create(true))
			{
				RepositoryCanTrackAddresses(tester);
				tester.ReloadRepository(true);
				var keyInfo = tester.Repository.GetKeyInformation(pubKey.GetLines().Skip(1).First().Derive(26).ScriptPubKey);
				Assert.NotNull(keyInfo);
				Assert.Equal(new KeyPath("1/26"), keyInfo.KeyPath);
				Assert.Equal(keyInfo.RootKey.GetHash(), pubKey.GetHash());
				keyInfo = tester.Repository.GetKeyInformation(pubKey.GetLines().Skip(1).First().Derive(27).ScriptPubKey);
				Assert.Null(keyInfo);

			}
			using(var tester = RepositoryTester.Create(false))
			{
				RepositoryCanTrackAddresses(tester);
			}
		}

		static IDerivationStrategy pubKey = new DirectDerivationStrategy(new ExtKey().Neuter());
		private static void RepositoryCanTrackAddresses(RepositoryTester tester)
		{

			tester.Repository.MarkAsUsed(new KeyInformation(pubKey));
			var keyInfo = tester.Repository.GetKeyInformation(pubKey.GetLines().Skip(0).First().Derive(0).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("0/0"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.RootKey.GetHash(), pubKey.GetHash());

			keyInfo = tester.Repository.GetKeyInformation(pubKey.GetLines().Skip(0).First().Derive(1).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("0/1"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.RootKey.GetHash(), pubKey.GetHash());

			keyInfo = tester.Repository.GetKeyInformation(pubKey.GetLines().Skip(0).First().Derive(19).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("0/19"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.RootKey.GetHash(), pubKey.GetHash());


			keyInfo = tester.Repository.GetKeyInformation(pubKey.GetLines().Skip(1).First().Derive(19).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/19"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.RootKey.GetHash(), pubKey.GetHash());

			keyInfo = tester.Repository.GetKeyInformation(pubKey.GetLines().Skip(0).First().Derive(20).ScriptPubKey);
			Assert.Null(keyInfo);
			keyInfo = tester.Repository.GetKeyInformation(pubKey.GetLines().Skip(1).First().Derive(20).ScriptPubKey);
			Assert.Null(keyInfo);

			tester.Repository.MarkAsUsed(new KeyInformation(pubKey, new KeyPath("1/5")));
			keyInfo = tester.Repository.GetKeyInformation(pubKey.GetLines().Skip(1).First().Derive(25).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/25"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.RootKey.GetHash(), pubKey.GetHash());

			keyInfo = tester.Repository.GetKeyInformation(pubKey.GetLines().Skip(1).First().Derive(26).ScriptPubKey);
			Assert.Null(keyInfo);

			keyInfo = tester.Repository.GetKeyInformation(pubKey.GetLines().Skip(0).First().Derive(20).ScriptPubKey);
			Assert.Null(keyInfo);


			tester.Repository.MarkAsUsed(new KeyInformation(pubKey, new KeyPath("1/6")));
			keyInfo = tester.Repository.GetKeyInformation(pubKey.GetLines().Skip(1).First().Derive(26).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/26"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.RootKey.GetHash(), pubKey.GetHash());
			keyInfo = tester.Repository.GetKeyInformation(pubKey.GetLines().Skip(1).First().Derive(27).ScriptPubKey);
			Assert.Null(keyInfo);

			//No op
			tester.Repository.MarkAsUsed(new KeyInformation(pubKey, new KeyPath("1/6")));
			keyInfo = tester.Repository.GetKeyInformation(pubKey.GetLines().Skip(1).First().Derive(26).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/26"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.RootKey.GetHash(), pubKey.GetHash());
			keyInfo = tester.Repository.GetKeyInformation(pubKey.GetLines().Skip(1).First().Derive(27).ScriptPubKey);
			Assert.Null(keyInfo);
		}

		[Fact]
		public void ShouldBlockIfNoChange()
		{
			using(var tester = ServerTester.Create())
			{
				var bob = new DirectDerivationStrategy(new ExtKey().Neuter());
				var utxo = tester.Client.Sync(bob, null, true);
				Stopwatch watch = new Stopwatch();
				watch.Start();
				var result = tester.Client.Sync(bob, utxo);
				watch.Stop();
				Assert.True(watch.Elapsed > TimeSpan.FromSeconds(10));
			}
		}


		[Fact]
		public void CanGetUnusedAddresses()
		{
			using(var tester = ServerTester.Create())
			{
				var bob = new DirectDerivationStrategy(new BitcoinExtKey(new ExtKey(), tester.Runtime.Network).Neuter());
				var utxo = tester.Client.Sync(bob, null, null, true); //Track things do not wait

				var a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);
				Assert.NotNull(a1);
				Assert.Equal(a1.Address, tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0).Address);
				Assert.Equal(a1.Address, bob.Root.Derive(new KeyPath("0/0")).PubKey.Hash.GetAddress(tester.Runtime.Network));

				var a2 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, skip: 1);
				Assert.Equal(a2.Address, bob.Root.Derive(new KeyPath("0/1")).PubKey.Hash.GetAddress(tester.Runtime.Network));

				var a3 = tester.Client.GetUnused(bob, DerivationFeature.Change, skip: 0);
				Assert.Equal(a3.Address, bob.Root.Derive(new KeyPath("1/0")).PubKey.Hash.GetAddress(tester.Runtime.Network));

				Assert.Null(tester.Client.GetUnused(bob, DerivationFeature.Change, skip: 30));

				a3 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, skip: 2);
				Assert.Equal(new KeyPath("0/2"), a3.KeyPath);

				//   0/0 and 0/2 used
				tester.Runtime.RPC.SendToAddressAsync(a1.Address, Money.Coins(1.0m));
				utxo = tester.Client.Sync(bob, utxo); //Wait tx received
				tester.Runtime.RPC.SendToAddressAsync(a3.Address, Money.Coins(1.0m));
				utxo = tester.Client.Sync(bob, utxo); //Wait tx received

				a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);
				Assert.Equal(a1.Address, bob.Root.Derive(new KeyPath("0/1")).PubKey.Hash.GetAddress(tester.Runtime.Network));
				a2 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, skip: 1);
				Assert.Equal(a2.Address, bob.Root.Derive(new KeyPath("0/3")).PubKey.Hash.GetAddress(tester.Runtime.Network));
			}
		}

		[Fact]
		public void CanTrack4()
		{
			using(var tester = ServerTester.Create())
			{
				var bob = new BitcoinExtKey(new ExtKey(), tester.Runtime.Network);
				var alice = new BitcoinExtKey(new ExtKey(), tester.Runtime.Network);

				var bobPubKey = new DirectDerivationStrategy(bob.Neuter());
				var alicePubKey = new DirectDerivationStrategy(alice.Neuter());

				var utxoAlice = tester.Client.Sync(alicePubKey, null, null, true); //Track things do not wait
				var utxoBob = tester.Client.Sync(bobPubKey, null, null, true); //Track things do not wait
				Assert.False(utxoAlice.Confirmed.Reset);

				var id = tester.Runtime.RPC.SendToAddress(AddressOf(alice, "0/1"), Money.Coins(1.0m));
				id = tester.Runtime.RPC.SendToAddress(AddressOf(bob, "0/2"), Money.Coins(0.1m));
				utxoAlice = tester.Client.Sync(alicePubKey, utxoAlice);
				utxoBob = tester.Client.Sync(bobPubKey, utxoBob);
				Assert.True(utxoAlice.Unconfirmed.Reset);

				tester.Runtime.RPC.Generate(1);

				utxoAlice = tester.Client.Sync(alicePubKey, utxoAlice);
				utxoBob = tester.Client.Sync(bobPubKey, utxoBob);
				Assert.True(utxoAlice.Confirmed.Reset);

				LockTestCoins(tester.Runtime.RPC);
				tester.Runtime.RPC.ImportPrivKey(PrivateKeyOf(alice, "0/1"));
				tester.Runtime.RPC.SendToAddress(AddressOf(bob, "0/3"), Money.Coins(0.6m));

				utxoAlice = tester.Client.Sync(alicePubKey, utxoAlice);
				utxoBob = tester.Client.Sync(bobPubKey, utxoBob);
				Assert.True(utxoAlice.Unconfirmed.Reset);

				utxoAlice = tester.Client.Sync(alicePubKey, utxoAlice, true);
				Assert.False(utxoAlice.Unconfirmed.Reset);

				tester.Runtime.RPC.Generate(1);

				utxoAlice = tester.Client.Sync(alicePubKey, utxoAlice);
				utxoBob = tester.Client.Sync(bobPubKey, utxoBob);

				Assert.False(utxoAlice.Confirmed.Reset);
				Assert.Equal(1, utxoAlice.Confirmed.SpentOutpoints.Count);
				Assert.Equal(0, utxoAlice.Confirmed.UTXOs.Count);

				Assert.Equal(0, utxoBob.Confirmed.SpentOutpoints.Count);
				Assert.Equal(1, utxoBob.Confirmed.UTXOs.Count);
				Assert.Equal("0/3", utxoBob.Confirmed.UTXOs[0].KeyPath.ToString());
			}
		}
		[Fact]
		public void CanTrack3()
		{
			using(var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Runtime.Network);
				var pubkey = new DirectDerivationStrategy(key.Neuter());
				tester.Client.Sync(pubkey, null, null, true); //Track things do not wait
				var id = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/0"), Money.Coins(1.0m));
				id = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/1"), Money.Coins(1.1m));
				id = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/2"), Money.Coins(1.2m));

				UTXOChanges utxo = null;

				while(utxo == null || utxo.Unconfirmed.UTXOs.Count != 3)
				{
					utxo = tester.Client.Sync(pubkey, null, null);
				}

				tester.Runtime.RPC.Generate(1);

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
				var key = new BitcoinExtKey(new ExtKey(), tester.Runtime.Network);
				var pubkey = new DirectDerivationStrategy(key.Neuter());
				var utxo = tester.Client.Sync(pubkey, null, null, true); //Track things do not wait

				var addresses = new HashSet<Script>();
				tester.Runtime.RPC.ImportPrivKey(PrivateKeyOf(key, "0/0"));
				var id = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/0"), Money.Coins(1.0m));
				addresses.Add(AddressOf(key, "0/0").ScriptPubKey);

				utxo = tester.Client.Sync(pubkey, utxo);

				var coins = Money.Coins(1.0m);
				int i = 0;
				for(i = 0; i < 20; i++)
				{
					LockTestCoins(tester.Runtime.RPC, addresses);
					var spendable = tester.Runtime.RPC.ListUnspent(0, 0);
					coins = coins - Money.Coins(0.001m);
					var destination = AddressOf(key, $"0/{i + 1}");

					tester.Runtime.RPC.ImportPrivKey(PrivateKeyOf(key, $"0/{i + 1}"));
					tester.Runtime.RPC.SendToAddress(destination, coins);
					addresses.Add(destination.ScriptPubKey);
				}

				while(true)
				{
					utxo = tester.Client.Sync(pubkey, utxo);
					if(!utxo.HasChanges)
						Assert.False(true, "should have changes");
					Assert.False(utxo.Confirmed.Reset);
					Assert.True(utxo.Unconfirmed.Reset);
					Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
					if(new KeyPath($"0/{i}").Equals(utxo.Unconfirmed.UTXOs[0].KeyPath))
						break;
				}

				tester.Runtime.RPC.Generate(1);

				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.Equal(1, utxo.Confirmed.UTXOs.Count);
				Assert.True(utxo.Confirmed.Reset);
				Assert.Equal(0, utxo.Confirmed.SpentOutpoints.Count);
			}
		}

		[Fact]
		public void CanTrack2()
		{
			using(var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Runtime.Network);
				var pubkey = new DirectDerivationStrategy(key.Neuter());
				var utxo = tester.Client.Sync(pubkey, null, null, true); //Track things do not wait
				var id = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/0"), Money.Coins(1.0m));
				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.False(utxo.Confirmed.Reset);
				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);


				var randomDude = new Key();
				LockTestCoins(tester.Runtime.RPC);
				tester.Runtime.RPC.ImportPrivKey(PrivateKeyOf(key, "0/0"));
				tester.Runtime.RPC.SendToAddress(AddressOf(key, "1/0"), Money.Coins(0.6m));

				utxo = tester.Client.Sync(pubkey, utxo);

				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(0, utxo.Unconfirmed.SpentOutpoints.Count);

				tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/0"), Money.Coins(0.15m));

				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.Equal(2, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(0, utxo.Unconfirmed.SpentOutpoints.Count);
			}
		}

		[Fact]
		public void CanTrack()
		{
			using(var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Runtime.Network);
				var pubkey = new DirectDerivationStrategy(key.Neuter());

				var utxo = tester.Client.Sync(pubkey, null, null, true); //Track things do not wait
				var gettingUTXO = tester.Client.SyncAsync(pubkey, utxo);
				var txId = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/0"), Money.Coins(1.0m));
				utxo = gettingUTXO.GetAwaiter().GetResult();
				Assert.Equal(103, utxo.CurrentHeight);

				Assert.False(utxo.Confirmed.Reset);
				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(txId, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				Assert.Equal(0, utxo.Unconfirmed.UTXOs[0].Confirmations);
				Assert.Equal(0, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(uint256.Zero, utxo.Confirmed.Hash);
				Assert.Equal(utxo.Unconfirmed.GetHash(), utxo.Unconfirmed.Hash);

				tester.Runtime.RPC.Generate(1);
				var prevUtxo = utxo;
				utxo = tester.Client.Sync(pubkey, prevUtxo);
				Assert.True(utxo.Unconfirmed.Reset);
				Assert.Equal(0, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(1, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(txId, utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.Equal(1, utxo.Confirmed.UTXOs[0].Confirmations);
				var bestBlockHash = tester.Runtime.RPC.GetBestBlockHash();
				Assert.Equal(bestBlockHash, utxo.Confirmed.Hash);

				txId = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/1"), Money.Coins(1.0m));

				prevUtxo = utxo;
				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.Equal(0, utxo.Confirmed.UTXOs.Count);
				Assert.False(utxo.Confirmed.Reset);
				Assert.True(utxo.HasChanges);
				Assert.True(utxo.Unconfirmed.Reset);
				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(txId, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				utxo = tester.Client.Sync(pubkey, null, null, true);

				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/1"), utxo.Unconfirmed.UTXOs[0].KeyPath);
				Assert.Equal(1, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/0"), utxo.Confirmed.UTXOs[0].KeyPath);
				Assert.Equal(bestBlockHash, utxo.Confirmed.Hash);

				utxo = tester.Client.Sync(pubkey, utxo.Confirmed.Hash, null, true);
				Assert.False(utxo.Confirmed.Reset);
				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(0, utxo.Confirmed.UTXOs.Count);

				utxo = tester.Client.Sync(pubkey, null, utxo.Unconfirmed.Hash, true);
				Assert.True(utxo.Confirmed.Reset);
				Assert.Equal(0, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(1, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(1, utxo.Confirmed.UTXOs[0].Confirmations);
				tester.Runtime.RPC.Generate(1);

				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.Equal(1, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/1"), utxo.Confirmed.UTXOs[0].KeyPath);

				var outpoint01 = utxo.Confirmed.UTXOs[0].Outpoint;

				txId = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/2"), Money.Coins(1.0m));
				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(0, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/2"), utxo.Unconfirmed.UTXOs[0].KeyPath);
				tester.Runtime.RPC.Generate(1);

				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.Equal(1, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/2"), utxo.Confirmed.UTXOs[0].KeyPath);


				utxo = tester.Client.Sync(pubkey, utxo, true);
				Assert.True(!utxo.HasChanges);

				var before01Spend = utxo.Confirmed.Hash;

				LockTestCoins(tester.Runtime.RPC);
				tester.Runtime.RPC.ImportPrivKey(PrivateKeyOf(key, "0/1"));
				txId = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/3"), Money.Coins(0.5m));

				utxo = tester.Client.Sync(pubkey, utxo);
				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/3"), utxo.Unconfirmed.UTXOs[0].KeyPath);
				Assert.Equal(1, utxo.Unconfirmed.SpentOutpoints.Count);

				utxo = tester.Client.Sync(pubkey, utxo, true);
				Assert.False(utxo.HasChanges);
				tester.Runtime.RPC.Generate(1);

				utxo = tester.Client.Sync(pubkey, before01Spend, utxo.Unconfirmed.Hash);
				Assert.True(utxo.Unconfirmed.HasChanges);
				Assert.Equal(1, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/3"), utxo.Confirmed.UTXOs[0].KeyPath);
				Assert.Equal(1, utxo.Confirmed.SpentOutpoints.Count);
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
		public void CanBroadcast()
		{
			using(var tester = ServerTester.Create())
			{
				var tx = new Transaction();
				tx.Outputs.Add(new TxOut(Money.Coins(1.0m), new Key()));
				var funded = tester.User1.CreateRPCClient().FundRawTransaction(tx);
				var signed = tester.User1.CreateRPCClient().SignRawTransaction(funded.Transaction);
				var result = tester.Client.Broadcast(signed);
				Assert.True(result.Success);
				signed.Inputs[0].PrevOut.N = 999;
				result = tester.Client.Broadcast(signed);
				Assert.False(result.Success);
			}
		}
	}
}
