using ElementsExplorer.Logging;
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

namespace ElementsExplorer.Tests
{
	public class UnitTest1
	{
		public UnitTest1(ITestOutputHelper output)
		{
			Logs.Configure(new TestOutputHelperFactory(output));
		}


		[Fact]
		public void CanSetAssetName()
		{
			using(var tester = RepositoryTester.Create(true))
			{
				var assetId = Hashes.Hash256(RandomUtils.GetBytes(32));
				var assetId2 = Hashes.Hash256(RandomUtils.GetBytes(32));
				var result = tester.Repository.SetAssetName(new NamedIssuance() { AssetId = assetId, Name = "hello" });
				Assert.Equal(Repository.SetNameResult.Success, result);

				result = tester.Repository.SetAssetName(new NamedIssuance() { AssetId = assetId, Name = "hello2" });
				Assert.Equal(Repository.SetNameResult.AssetIdAlreadyClaimedAName, result);

				result = tester.Repository.SetAssetName(new NamedIssuance() { AssetId = assetId2, Name = "hello" });
				Assert.Equal(Repository.SetNameResult.AssetNameAlreadyExist, result);

				Assert.Null(tester.Repository.GetAssetName(assetId2));
				Assert.Equal("hello", tester.Repository.GetAssetName(assetId));
			}
		}

		[Fact]
		public void RepositoryCanTrackAddresses()
		{
			using(var tester = RepositoryTester.Create(true))
			{
				RepositoryCanTrackAddresses(tester);
				tester.ReloadRepository(true);
				var keyInfo = tester.Repository.GetKeyInformation(pubKey.Derive(new KeyPath("1/26")).PubKey.Hash.ScriptPubKey);
				Assert.NotNull(keyInfo);
				Assert.Equal(new KeyPath("1/26"), keyInfo.KeyPath);
				Assert.True(keyInfo.RootKey.SequenceEqual(pubKey.ToBytes()));
				keyInfo = tester.Repository.GetKeyInformation(pubKey.Derive(new KeyPath("1/27")).PubKey.Hash.ScriptPubKey);
				Assert.Null(keyInfo);

			}
			using(var tester = RepositoryTester.Create(false))
			{
				RepositoryCanTrackAddresses(tester);
			}
		}

		static ExtPubKey pubKey = new ExtKey().Neuter();
		private static void RepositoryCanTrackAddresses(RepositoryTester tester)
		{

			tester.Repository.MarkAsUsed(new KeyInformation(pubKey));
			var keyInfo = tester.Repository.GetKeyInformation(pubKey.Derive(new KeyPath("0/0")).PubKey.Hash.ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("0/0"), keyInfo.KeyPath);
			Assert.True(keyInfo.RootKey.SequenceEqual(pubKey.ToBytes()));

			keyInfo = tester.Repository.GetKeyInformation(pubKey.Derive(new KeyPath("0/1")).PubKey.Hash.ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("0/1"), keyInfo.KeyPath);
			Assert.True(keyInfo.RootKey.SequenceEqual(pubKey.ToBytes()));

			keyInfo = tester.Repository.GetKeyInformation(pubKey.Derive(new KeyPath("0/19")).PubKey.Hash.ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("0/19"), keyInfo.KeyPath);
			Assert.True(keyInfo.RootKey.SequenceEqual(pubKey.ToBytes()));


			keyInfo = tester.Repository.GetKeyInformation(pubKey.Derive(new KeyPath("1/19")).PubKey.Hash.ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/19"), keyInfo.KeyPath);
			Assert.True(keyInfo.RootKey.SequenceEqual(pubKey.ToBytes()));

			keyInfo = tester.Repository.GetKeyInformation(pubKey.Derive(new KeyPath("0/20")).PubKey.Hash.ScriptPubKey);
			Assert.Null(keyInfo);
			keyInfo = tester.Repository.GetKeyInformation(pubKey.Derive(new KeyPath("1/20")).PubKey.Hash.ScriptPubKey);
			Assert.Null(keyInfo);

			tester.Repository.MarkAsUsed(new KeyInformation(pubKey, new KeyPath("1/5")));
			keyInfo = tester.Repository.GetKeyInformation(pubKey.Derive(new KeyPath("1/25")).PubKey.Hash.ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/25"), keyInfo.KeyPath);
			Assert.True(keyInfo.RootKey.SequenceEqual(pubKey.ToBytes()));

			keyInfo = tester.Repository.GetKeyInformation(pubKey.Derive(new KeyPath("1/26")).PubKey.Hash.ScriptPubKey);
			Assert.Null(keyInfo);

			keyInfo = tester.Repository.GetKeyInformation(pubKey.Derive(new KeyPath("0/20")).PubKey.Hash.ScriptPubKey);
			Assert.Null(keyInfo);


			tester.Repository.MarkAsUsed(new KeyInformation(pubKey, new KeyPath("1/6")));
			keyInfo = tester.Repository.GetKeyInformation(pubKey.Derive(new KeyPath("1/26")).PubKey.Hash.ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/26"), keyInfo.KeyPath);
			Assert.True(keyInfo.RootKey.SequenceEqual(pubKey.ToBytes()));
			keyInfo = tester.Repository.GetKeyInformation(pubKey.Derive(new KeyPath("1/27")).PubKey.Hash.ScriptPubKey);
			Assert.Null(keyInfo);

			//No op
			tester.Repository.MarkAsUsed(new KeyInformation(pubKey, new KeyPath("1/6")));
			keyInfo = tester.Repository.GetKeyInformation(pubKey.Derive(new KeyPath("1/26")).PubKey.Hash.ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/26"), keyInfo.KeyPath);
			Assert.True(keyInfo.RootKey.SequenceEqual(pubKey.ToBytes()));
			keyInfo = tester.Repository.GetKeyInformation(pubKey.Derive(new KeyPath("1/27")).PubKey.Hash.ScriptPubKey);
			Assert.Null(keyInfo);
		}


		[Fact]
		public void CanGetAssetName()
		{
			using(var tester = ServerTester.Create())
			{
				var assetIssuance = tester.Runtime.RPC.IssueAsset(10, 10, true, "hello");
				Thread.Sleep(500);
				Assert.Equal("hello", tester.Client.GetAssetName(assetIssuance.AssetType));
				Assert.Equal("", tester.Client.GetAssetName(uint256.Zero));

				//Can't claim a name already claimed
				var assetIssuance2 = tester.Runtime.RPC.IssueAsset(10, 10, true, "hello");
				Thread.Sleep(500);
				Assert.Equal("hello", tester.Client.GetAssetName(assetIssuance.AssetType));
				Assert.Equal("", tester.Client.GetAssetName(assetIssuance2.AssetType));
			}
		}


		[Fact]
		public void CanTrack4()
		{
			using(var tester = ServerTester.Create())
			{
				var bob = new BitcoinExtKey(new ExtKey(), tester.Runtime.Network);
				var alice = new BitcoinExtKey(new ExtKey(), tester.Runtime.Network);
				var utxoAlice = tester.Client.Sync(alice.Neuter(), null, null, true); //Track things do not wait
				var utxoBob = tester.Client.Sync(bob.Neuter(), null, null, true); //Track things do not wait
				Assert.False(utxoAlice.Confirmed.Reset);

				var id = tester.Runtime.RPC.SendToAddress(AddressOf(alice, "0/1"), Money.Coins(1.0m));
				id = tester.Runtime.RPC.SendToAddress(AddressOf(bob, "0/2"), Money.Coins(0.1m));
				utxoAlice = tester.Client.Sync(alice.Neuter(), utxoAlice);
				utxoBob = tester.Client.Sync(bob.Neuter(), utxoBob);
				Assert.True(utxoAlice.Unconfirmed.Reset);

				tester.Runtime.RPC.Generate(1);

				utxoAlice = tester.Client.Sync(alice.Neuter(), utxoAlice);
				utxoBob = tester.Client.Sync(bob.Neuter(), utxoBob);
				Assert.True(utxoAlice.Confirmed.Reset);

				LockTestCoins(tester.Runtime.RPC);
				tester.Runtime.RPC.ImportPrivKey(PrivateKeyOf(alice, "0/1"));
				tester.Runtime.RPC.SendToAddress(AddressOf(bob, "0/3"), Money.Coins(0.6m));

				utxoAlice = tester.Client.Sync(alice.Neuter(), utxoAlice);
				utxoBob = tester.Client.Sync(bob.Neuter(), utxoBob);
				Assert.True(utxoAlice.Unconfirmed.Reset);

				utxoAlice = tester.Client.Sync(alice.Neuter(), utxoAlice, true);
				Assert.False(utxoAlice.Unconfirmed.Reset);

				tester.Runtime.RPC.Generate(1);

				utxoAlice = tester.Client.Sync(alice.Neuter(), utxoAlice);
				utxoBob = tester.Client.Sync(bob.Neuter(), utxoBob);

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
				tester.Client.Sync(key.Neuter(), null, null, true); //Track things do not wait
				var id = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/0"), Money.Coins(1.0m));
				id = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/1"), Money.Coins(1.1m));
				id = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/2"), Money.Coins(1.2m));

				UTXOChanges utxo = null;

				while(utxo == null || utxo.Unconfirmed.UTXOs.Count != 3)
				{
					utxo = tester.Client.Sync(key.Neuter(), null, null);
				}

				tester.Runtime.RPC.Generate(1);

				utxo = tester.Client.Sync(key.Neuter(), utxo);
				Assert.True(utxo.HasChanges);
				Assert.Equal(3, utxo.Confirmed.UTXOs.Count);
				utxo = tester.Client.Sync(key.Neuter(), utxo, true);
				Assert.False(utxo.HasChanges);
			}
		}


		[Fact]
		public void CanTrackSeveralTransactions()
		{
			using(var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Runtime.Network);
				var utxo = tester.Client.Sync(key.Neuter(), null, null, true); //Track things do not wait

				var addresses = new HashSet<Script>();
				tester.Runtime.RPC.ImportPrivKey(PrivateKeyOf(key, "0/0"));
				var id = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/0"), Money.Coins(1.0m));
				addresses.Add(AddressOf(key, "0/0").ScriptPubKey);

				utxo = tester.Client.Sync(key.Neuter(), utxo);

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
					utxo = tester.Client.Sync(key.Neuter(), utxo);
					if(!utxo.HasChanges)
						Assert.False(true, "should have changes");
					Assert.False(utxo.Confirmed.Reset);
					Assert.True(utxo.Unconfirmed.Reset);
					Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
					if(new KeyPath($"0/{i}").Equals(utxo.Unconfirmed.UTXOs[0].KeyPath))
						break;
				}

				tester.Runtime.RPC.Generate(1);

				utxo = tester.Client.Sync(key.Neuter(), utxo);
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
				var utxo = tester.Client.Sync(key.Neuter(), null, null, true); //Track things do not wait
				var id = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/0"), Money.Coins(1.0m));
				utxo = tester.Client.Sync(key.Neuter(), utxo);
				Assert.False(utxo.Confirmed.Reset);
				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);


				var randomDude = new Key();
				LockTestCoins(tester.Runtime.RPC);
				tester.Runtime.RPC.ImportPrivKey(PrivateKeyOf(key, "0/0"));
				tester.Runtime.RPC.SendToAddress(AddressOf(key, "1/0"), Money.Coins(0.6m));

				utxo = tester.Client.Sync(key.Neuter(), utxo);

				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(0, utxo.Unconfirmed.SpentOutpoints.Count);

				tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/0"), Money.Coins(0.15m));

				utxo = tester.Client.Sync(key.Neuter(), utxo);
				Assert.Equal(2, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(0, utxo.Unconfirmed.SpentOutpoints.Count);
			}
		}

		[Fact]
		public void CanExtractAssetName()
		{
			var result = NamedIssuance.Extract(_NamedIssuance);
			Assert.NotNull(result);
			Assert.Equal("f0a175f775dab46af488a80b9a4c020f39a7f42ba4c57c6d91b7cb83ca98abf0", result.AssetId.ToString());
			Assert.Equal("test", result.Name);
			Assert.Null(NamedIssuance.Extract(_GoodTransaction));
		}

		[Fact]
		public void CanTrack()
		{
			using(var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Runtime.Network);
				var utxo = tester.Client.Sync(key.Neuter(), null, null, true); //Track things do not wait
				var gettingUTXO = tester.Client.SyncAsync(key.Neuter(), utxo);
				var txId = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/0"), Money.Coins(1.0m));
				utxo = gettingUTXO.GetAwaiter().GetResult();


				Assert.False(utxo.Confirmed.Reset);
				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(0, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(uint256.Zero, utxo.Confirmed.Hash);
				Assert.Equal(utxo.Unconfirmed.GetHash(), utxo.Unconfirmed.Hash);

				tester.Runtime.RPC.Generate(1);
				var prevUtxo = utxo;
				utxo = tester.Client.Sync(key.Neuter(), prevUtxo);
				Assert.True(utxo.Unconfirmed.Reset);
				Assert.Equal(0, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(1, utxo.Confirmed.UTXOs.Count);
				var bestBlockHash = tester.Runtime.RPC.GetBestBlockHash();
				Assert.Equal(bestBlockHash, utxo.Confirmed.Hash);

				txId = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/1"), Money.Coins(1.0m));

				prevUtxo = utxo;
				utxo = tester.Client.Sync(key.Neuter(), utxo);
				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(0, utxo.Confirmed.UTXOs.Count);
				utxo = tester.Client.Sync(key.Neuter(), null, null, true);

				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/1"), utxo.Unconfirmed.UTXOs[0].KeyPath);
				Assert.Equal(1, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/0"), utxo.Confirmed.UTXOs[0].KeyPath);
				Assert.Equal(bestBlockHash, utxo.Confirmed.Hash);

				utxo = tester.Client.Sync(key.Neuter(), utxo.Confirmed.Hash, null, true);
				Assert.False(utxo.Confirmed.Reset);
				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(0, utxo.Confirmed.UTXOs.Count);

				utxo = tester.Client.Sync(key.Neuter(), null, utxo.Unconfirmed.Hash, true);
				Assert.True(utxo.Confirmed.Reset);
				Assert.Equal(0, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(1, utxo.Confirmed.UTXOs.Count);
				tester.Runtime.RPC.Generate(1);

				utxo = tester.Client.Sync(key.Neuter(), utxo);
				Assert.Equal(1, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/1"), utxo.Confirmed.UTXOs[0].KeyPath);

				var outpoint01 = utxo.Confirmed.UTXOs[0].Outpoint;

				txId = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/2"), Money.Coins(1.0m));
				utxo = tester.Client.Sync(key.Neuter(), utxo);
				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(0, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/2"), utxo.Unconfirmed.UTXOs[0].KeyPath);
				tester.Runtime.RPC.Generate(1);

				utxo = tester.Client.Sync(key.Neuter(), utxo);
				Assert.Equal(1, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/2"), utxo.Confirmed.UTXOs[0].KeyPath);


				utxo = tester.Client.Sync(key.Neuter(), utxo, true);
				Assert.True(!utxo.HasChanges);

				var before01Spend = utxo.Confirmed.Hash;

				LockTestCoins(tester.Runtime.RPC);
				tester.Runtime.RPC.ImportPrivKey(PrivateKeyOf(key, "0/1"));
				txId = tester.Runtime.RPC.SendToAddress(AddressOf(key, "0/3"), Money.Coins(0.5m));

				utxo = tester.Client.Sync(key.Neuter(), utxo);
				Assert.Equal(1, utxo.Unconfirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/3"), utxo.Unconfirmed.UTXOs[0].KeyPath);
				Assert.Equal(1, utxo.Unconfirmed.SpentOutpoints.Count);

				utxo = tester.Client.Sync(key.Neuter(), utxo, true);
				Assert.False(utxo.HasChanges);
				tester.Runtime.RPC.Generate(1);

				utxo = tester.Client.Sync(key.Neuter(), before01Spend, utxo.Unconfirmed.Hash);
				Assert.True(utxo.Unconfirmed.HasChanges);
				Assert.Equal(1, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/3"), utxo.Confirmed.UTXOs[0].KeyPath);
				Assert.Equal(1, utxo.Confirmed.SpentOutpoints.Count);
				Assert.Equal(outpoint01, utxo.Confirmed.SpentOutpoints[0]);

				utxo = tester.Client.Sync(key.Neuter(), utxo, true);
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
			var address = key.ExtKey.Derive(new KeyPath(path)).Neuter().PubKey.Hash.GetAddress(key.Network);
			var indexes = new KeyPath(path).Indexes;
			indexes[0] = indexes[0] + 2;
			var blinding = key.ExtKey.Derive(new KeyPath(indexes)).Neuter().PubKey;
			return address.AddBlindingKey(blinding);
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
				Assert.True(tester.Client.Broadcast(signed));
				signed.Inputs[0].PrevOut.N = 999;
				Assert.False(tester.Client.Broadcast(signed));
			}
		}



		Transaction _GoodTransaction = new Transaction("02000000000101f4733a0e18ee9d316e95a63cae43f5e8e76bc89df43a4c8cadbe7ee88eed9c382f00000000fdffffff030b04e1f9a22fa08a51b2c1a1150b2be23deabe0fc0d4ab0f0dad07138b29642d8c08598ee98213cb7669d142371c4cffac1d70972603785c3ece0df8f9b295b1dc42024304028a3d489a0175bbfc8a363c5217929fb2192de55b57b8a586dcb381ebb11976a914a5ea514f7623e63c69ba54c2c0563e10468f0dde88ac0aaf713d5e9a8520acdc468d828db9fa5b47b28ed1ee54b616439f73d2ef46298108b2e21367daec32b1c23ff581bfa0af057b96771a1eb9c5f70ff34dbee09037cc02db2441d5aa3e804fe4260fe9562612f4f82cbaeaeafd1b061632e876538e26431976a914c104ee318aac1d6d93dfde8a53d6270aceea67dc88ac0121667c3dcc51290904a6a9eae27337e6ff5602d0deb5ca501f77be96de63f609010000000000009704000000000043010001051be2c37e28c289141c9dc4ed9d3e0fcdd957fa9e78dee00a1473d0497b029432e0cf780b71c3c15786817f9d1e66cdd2dba23d0aaee029915cb0e21ec91597fd2d0e602c0000000000000001130e1805e28272bf8baf4ccc99eb8c32f31e8d2fb44c616d73921cacafc5bd65da9186b54a2567576bea31a102f9e26721f629ef461899a10488c9beb7ee91ad62050e44789e0e7d8a5ac936860c8483b31edb579b718f3a0208ea5b25dcc64cccb909c6d2418da397dfe1d49b534734029eaa4c4794337519f7dbfb639800f228b5c82cddefcbc9202f7d09874d88bd0042fd122be1e17a717fb884ff82f2a5852c0788651430091c96d347c86f43a00dc2f9fbd72fab7886adc8534e3e855f4eb8f05d883a66f2bac73c3b7ed0de41d72a3c5cb5ca9b8b829145765ea73d1503dc381b9fe74f944ca0195d2e6f19ffce449ec693f4a7054e105b755ebaceed0be25feb20eca3087ae96a4fae2dd5ad3e6299b5446844b334ec73ab57b4545c31d47a25760277ff7ab7d607eef1281ddc53a77d0a0e61e005c8b8d1507a5eeba348e8cb1e1a5e0a30383ce6edf24fa665a3d2ac7be61a8022654ee1bc2b5ad3a291b44d7a3b801b7c1902561595ef4ea2ace96d1f0d1f41f5a6defa79ff87c8c2c67fd1e80497b67105638001081d285f9bbd22c1c96325802298dce711a473ad37eb8bc38fcb2dd78414601cbd3270f13220312ec02ec9b57838197c108171fbdbfae718ded083261e340a589bf2aa94873aa499ada0c87e56a62a59d74dd7ce55c56462704dc9a133f8f314cbe90e987ac120c3cfd08941c5fec0e30cef5307fd1f60b2a9e9aee742adaee4c4ef1b181b0b81f24f74a92ce4eb8d62c269061a4651363a41ab7ad86ea47ed5e7c5fe36ce72d5663e81f0de829bc3b37cd1d4ee407f2c949c17c31bcd1dd0959791c3aa1456a0807b98e97036933e55c093027fa99685263176287a5325734497318358d079d1d2ae5c0752c08c54499a41f56c96bd2c9781df125cd4fc8fabccb4edb47e8205cd5b76ef6de563571412e7ba747db6a85ffb1b687dc3a8c9b724c4ca0450574021c3ce623a7f27702eb24246a25cd2cea09efbb02ae27ca31e9242ef3f754ff1d09451fdf9c7d90d28dfe8649434b5eabedb50185f0533e406e5d2430850f40f225cb375ba135c87c654bac2da0c85c044d6c18e86286fce1f6ab73378248f5e215708a8cb65a15ba5c7592b03be388cc8402c593e0f1b34c6863c79ceb777cce9e2942dcfddc0e2a765efccdd093c0b0d700a440ad48a3dd1f5381b57bd74df47e339aa707f1e81b6b43f21880cdbe0ccd11be52e294860da8f4970fc2a5ff56cd0396a432a486ea658874677c18ac4099a71353486dc689025358a5da57f9dc53c053a72ad62c40ee89665a79595da29091d4afd066ad5dba105f14f2c50074e230479cdb8fc051c1c165d7b66232501b65291db6a1d33b5a4b0331251aef74769c6ba62ea61229760dfa2be39143f579f2ad52258f157a25ae5ee8abf5a32ea873fa4c08be28dda8db45a8d828c675f792b51568bfc3f5cfc5ebf525588160cf5dafcd18ff834e723c58fb54f9c1f6d00b4dcf9c4a9f13714ce0a59b6c227e18f71494409557cfb5e46406b34e116784076ffe525d41322a9eb750bc81628869c419a45e1c5116cedb08031b1baa97212f4ac2a39161a63020df52c955c1d0a6347df82106bc0e50bcbff5b0218a788aed3a0bcb01d4b0ffab5dbf89fe80ea6192e1bbbd70196448c1d14587870e7a39cb3adcc3741e15e78a33386f47a388fc23bae680650da26b28d2af33fef2727689a49dfcecc87983198d9e9ad5169c1ffa20099334a1c253903495098d3875fcef5205092d1a5e6539b7134693d19892aabe73b4d43d3cf17dbb139a877d315926728b133ada62b6aa8972fd7255d6c99c833d4c07f114ba3d3d449763cddf8303dde0089faf5bd0606e18eefa7d38d6ee630f15fcb36831323a0a9eb94455b26c68d6d3b28fa90b48507bb317f633c6fe130f0779dd8ff422b5150d691dc52c2623ace6805b1e9d87443a926a434923474679d8437b43a6163b61d22596d3c526291f9ae372d9a3904040c3933fbc20e4174e943c50f6cd9d72f81b5e9f3847b50ce882d3fa9623fcf0cef68c73552fd4ce88bba8460e669369b7b552643eb5e70e714adc40096b0ad466db4aa3efb45a184214a39e911ff38da2cf01b0968b88ea7e7400da1f30aff127fc083da3e2b853e4a589e6d3ab595ab3f02b30c9eef7c178eb491f75b2536863132316c5f7c3c983fac431b61688812b458d841b75493c419781cb21730b634edbbab07f82b202a71c3594a0b0b5e3f19932995391421ae6b70b5738d9d96af7a071e51a274e3746cf0089f52799749dbae47370bf222295b5ed54e15ed17f059dd4ad7793e03d495694f4239220e174c33a822555cdca4d72e3bd6b63bc7dc6e5586cdc7a9519883796c9396fe9beff635b41f53259612460d92766684566f67a27cd3c3352aa2e5ba36561fd103b6406dacb8e5ef9793917aba6648eb059e1777f559853fca386a1dc6e76a1888623ab59387c813440eb85366cfe3963c95d3ce6f4af5a105b0026a7545f983e5317313f60bfb65f17c5f51b7fcc14dc9192412f7fb927ad2897cd9ef23a31c069acfba77f42d421d1991f7f62655df36a78523312d246a0e40f457c8d42f801726c254f15d05b166f81ae37e2ebb8e2ef5c23a8cff7ea6207e968b1fbf84813ead890b5fad6f41010d7613c853da5f7cba5969446ed4b93efb9e61ebfc09211db16eb0b21ff6d0c1d99c6e24af9f957509da4d0f20f093a151d68487c1b7defb72c474564b2cb2622a2664e6daae1bc7d104d7d49e0bb5bd5d5899f3a3f120e235305cb6319471c22df7b26f74ec1488c51c121fb3831871803e96d52d465e30919d1d435fda0ad76b2d45a7f4ba17f1b88f230a18f89218618fe7103836f2a9c7fe4259c50c23ed8126cf6b6d4fe50dca590796bdde1b2c98c44e36a0bd6f75820fc9e6fc13a89db0f7fc7bedeae3447e68422e874d62b53ebb4ade616788f1cb89e65a997b059cee648e13db85863eba0bc50aa0b28becac03f30c9ba755685a9c75ce5de195fed2471b8593bd9136eb48f09e99f8845935dd8bd8528927138f337a156b346c3150348ff4829ddc27fb62e9678ad09051a42ad5290e6e2a470df1bd8f703778b5413fdcff213e5d7a1355687c132494c0077d8d862459fd9375720a86facc751731a1422f2bb6deaf15a3fa08357b4a8bb7370ba97f56a094b2373a2fea792edaaf5db841bbf7f035761c206dfc1dbd1482a111ec0cc5be24533ede87e29273e94952b2a73ed994a8bd1ef16ce00d00f00b472b5d27d790cf6a704aa1ab265f830052aeddd9a8a7f1fdc09cb15a8639008f854029c089720be983df6d1bc8ad1ea602cdea1d5b8b670ac55bc2d65a8a0d51020160fb83f2496c9a45346957986ff6de43c30603c9b37adb3055c62f6043a9134bebd4d1f8ea1c6172f5c6439be8a2c427c02250f47796989198f5aadceba730b7aafc3d010727eaba2cdb95a36549c8ac0957c7f938668a5393e33f72cecd9f7c48e751b6d556c66f02bbf72d806d37f9d0110ca43c6caffffa80556995ed614957351a5f30ba1e37f18d80dfc29585572ddd2572ecf9d57d41a47171e7cad6db74179ba11ed88ca7a459df458ff119383593d8488ee35aa796c7e52a82e5f7ead18ca96dec75e44d1fe3daabcae1df7af52e60df25dc864fb4b7ae60c3bb969647c739a32b45c003e6f46a8d946a37e7fd9f1ed73d225534e2d10005c1136554ea71d1af578f27460a2be17b868ab1656fda80c9d83418d589979e6fa22ca3ecee0156eb8b675c150310ada8865fcd99870e3f4dcc77630d6424dd24f3568950f614bb07dff0ab031308bcc8f522fa6d45b829621ec1f6307c9a6c3b14e5334de754cda544882b61932f7b43f752b36666f7d008e496610a696d1389257098f46f95f7fecf3c4286b9f75722a4b01f487aafbd092c4382338f29f69cd5baaf32114c4a718030e25d2fd2846914bec947af49e0231f94fa062013d55fa67d4e4abf4c1b78bbf43fa8f8c5267382a1ef3ce4b345baf45ed4771cd9d4208fc375f6c5002a1a5aeb586311eff8c804e27fe0ebb5c1668f579dba8e4f38944179fb1dbca913e7591ad76d312fb1b0905aeff987e9e79918c5a6ee836391ba43f3607b4c609b3e4eee43fd8281016f6bb6ab96084eb712daf79fde88564ea3707cc528ea5c5f7b5ff07a5080b7e9c563eccdd5dd81803f951b77b53a7698c2950237e682fe24d9b3c9ffbebd37c0b281e3284a3fd6aed2ca17d685ce2eb8370e558e674cd01ee15f75b53c9794aa7e821ccbca3eec306021642deadf2a6ae8ae24d44c917f33cd7764dfcf386c537b6cce5bff2e024f939d745c3f7f671bfbf730b60a935f28cc3927b09b37d18d7fa003bd2a4774fc84830c3abc5bc330f2b610f9e9d40313afcf2a65d545a7cb986b628d16a02a5912e6b4fcaf645e3997271f8c266d9887748a115e071061dc176d7f83835d2ba0d1d5399389189ca7d1046517c87b0b33255e6d43eebeee8100ceedd201af07db9766533eaab95ff4912d0a7145ce2505ac8269d3cb7bbc71e191ed68b4fe2457dd3f6d48c7ff3c9bd0b6754b62afae0091a2ab84876bfb09e03737dac3730a34c1c9b0b28bee170259f118fbbb2da90b16ef3f40a83446e147edf193e5038b453894c5f57839750bc5c998041c69f36257152dd600a8b5c58fa0eeb17f5a15266787341c5c076f5ebd8300b42715fa1d36a45dc04cf3f2c1922175cf0d9949ed51df44afb6079041d98f3292bb86ec3975913431572c65ae7a04ef3be552a8863e9c9511379b242ae60374ca17e0543c01a0195c24003ea1036ff9cb2f7f9e9854615606a65f99a16edd36d8cdd75bf493c1aeb86cd96a53ea62b15abab992f4aa0a2e8f479e1ec75bc004d0350151b55615f11e2cff25a3c1d351da207584ecb56b2c765f037a1fd0720fb0c79702872bdb9edb8e36d588e08327f95c6ca8e81f2f9c1d528cf757c7a769ab848f56a8c054a89533f02426605e43dde7768d0056fb9269cb9db4d060327ab7d3f43617931662a219113bf99a1f64ef0da03224a435c0dbb4dccc7f490ad34f06d369c4afbb192e739e43010001ae37d88a114c0fff9ebaf0873ea4a3a916111554be03476f32d67871b1c9520d5882bbee20dbabd87f0fce2ea0db485b4aefcfd1f4dc9ec7cc0294fd0efc5edafd0c0a601f0000000000000001cd4b78c911a48cf835ae1956f1751c6a06158a8548bd33d04a35dd8f5dd788fb2a57771680033364066662672c62c803cd22e9344efbe8b9b4b7ca8960c15eb8e03c2f62ad369fe9b0dbcde349aabf83a5af44d8715c7a9d7b8abe196cf3c94e4d4a0221901e66442273a3b3a261a81df44cdfc2bcdb2d1acdaef92e4b7d46708e3836e71c24e7377e775012ba4597cbeb98f0d468f7925387625e40778195550cd94026a017cd6180278cc2dd07019f148167a8694caf2ddee35cdebde9abb0fea438c2ef50720d2e83424d6737182953ba1e3cb14894b1fad190ef229777fda5bbd455137d2d6b09dea34083e3e97d7fcb82b6c217ddb229ee646e5cfd40d32c1c336fbcaf8806bdb1915a7ad808ff7b3d05a6b1b6064b067fa79a7d17b22af6904514a83445d6d76afc257e96c1103c5dbf18df57a80283102e7ef10d200e6d9e0f66b53bd04c50c3441f8dad7f97c87c3388c4549b7e024e983db19e565dd5c2b34577e5008be4d2b57fc795787e3e4999c0b26abfbdbec373f4efd41bb7771b6673b37f11d5e45fd7c06a973617f3303512d291a1d764bcc28595882cd5d56e688e79d4c8405638b53788788b0c190b62f4d346b97253da80ff96c362a95f72336026ca34aa30bbb971872bff997a639bd1c289a430d0a5545aa3d57ecbd6717527af4df3bb6f8ad101b506112e6ea1c4a76d3c3993384649cd0ac5bed5a4e5d378d93f3979c05885c1c9f38f396e033906e75bf8115e9f61f01654221bca53e6c1cb8e42a97daa3bd9c4dfb3183027eb418567e825aafdbd9e51ea5799b9e2b221e9f8796a22e5b0965b5f545bc10e0121e7ce5341662925e6089ea3bbb34f7e15cdd1b1eaa477cc07f2db560564c74cdb91a7cf1dbab4caf9dd4eb1df8f2d4edeb58f7af85d5d0e0f96acca204ac584e230761e0f53e579f3bf444f4837e488288e14df187a66441b13ca76d6a91aaf152578898fc70c83015c72a5fc19d85320b07f091ec4a537360e18a7a09fc8c6889848e5064195d2d7d95c92b90c2d4f8c581fff06d961c2146e11e81a8ba14b86bb0b1b5e9eac03dcb0d80cf95b829444577549cdbdc3c01eb45bf3a1b87529ee8acc64a488a3aaea1347e6b66587270f1b47c91844d99d0b20243e24aa455281e94b871164b78563fca819aeb57fccaf4d10a145efdae5e8813ed716e2439439f09a4302ec716414e8f6d96aaae7ba5696dfbd1235314a475657c724d8ba5dae6094ff51ce2813e65592ff4825b996d96185c44fbbbb10eab5ba2fdf79f901e942f85c1011efc6988a238f54a20316925148233564a64c25116f970bdf52547879d75dde0cd7e9d2a74f8c785b27b1c5135b3551291197c94f8f8a538cdc5fa768e30d2cc34bf9edfacd396064970345394173550f638f2e0d31ac9e3c0f48f035d465b301f0692a532cb4f5cf18154205d972c5b02b3b3edb760e7775f82905fb2d6381f3302b8944999deeadf7c9952c02ff06d58af6a84dd7b65f34603f8ff133b528bbda5ee5b11c0e23a0691303ac7719b80eb3469d22d214c7facadc7e255f8c07416c586c1f9816b784408d20eb6da79a734d2e9fb411d925088f3c8b924610db53afc6ea2e18cb0c436bc1650e2594420437b9f6cf8e694fe6964bc767116b0fe1071189f08e873643ad79cddcbf78585d7f37cf69cb690bf80765650aab67988c52c0e8fdb595a020e5922935333dfe63e3fc21e39b34743b498bbd31cc1e5f40a6e8bd3542f048c2aa0dd59d18064ce656b83fc3012e105f8fb947fa1326c2998209feb7771e8a138f998771b04ffa2970219810cf491acd35a718333d3e879d38efaf4321e24b4f81c2ddae3dc7ecdefd93c430aed62d62477ab752fdab1b1ef3f6d2fdb4a82878319c5faeb822dea4ed8ea0d5537e8f728f52c283c1cbad1c5228743a2f51d18542f6b71f1d7efe87794666d231d548574bcecaee41c5a68e7981459e5fb18b0f2ecc909b453607db7c388016e5d509373b442e1447e4dcd8079dbdea070324357865e42127787923f4c173703a27b66b5c09fa9b52a367463c4f603077574475bb8145a3bd2e5d9a188d4235d1c8bba0ea3b5b376b28817c19e75545c28bf4775b5ac02e04c61354ca4efa868eb756ee196590ea4d2187cfefb30db4e0fdf520d8b011a33920006739a9cec741f20a6fc666a412a5b86bc85012226b11ce6ad6f44dc3070e11af5442b72bda46035b2e2faffcc8399ed6b3c5fa1831c2e3e16b1866820b9403e647f030202a3ac81520e2abf2fb509fc47528f284107b8297d72dcaddcd87159018ec4e348177327ce16d318aae3fd88d0e01eff2e590b61b7ae6be06de4e538658dd2bd433d5567dfa4df71606e1cb3ad0a44f89f0c2ab16ae5e4a0b43bd213ace30333c7259d5cc9be3a6374ccd02811c8d172204d6b09fab99c5dab71f7fcca3720adda1897222a9f3aa91960617ac335a284fda9e604eb3f0a371a67c8bbc4395ccaef986862e9eff63f7e808796a158a3683611a8f40b450d9963cc25b5a8c83c376dac0dc7be36d500b9f9075d220c3d3e1a7951ec9159618e7228758c69b84854b6b06ce661902d478148dfc1f26a902b14ba45c22ee40cb36bf3dc08801998675c859007f5efce4169d4e057d188b25af6947e30a4c896516e03778acf8a3e8df237e3438b43bf0b7e136799dd9d150656f27846ea97e4876ef3c43063ec374d4186ac3d97d3b0223cd614f4dad74dc4f62669cb2ff5a5376b77b65ce5ade45aa3a819501fd78d7d11074097fb242e06eec80a3d4245b9e5193c1cf4363e60ec8b40ca9e400ace58e30afc485ca7ae7e064299d13d592faa55c465df154139512910ae359d9d101856483a7edbd56ed957955e79868ca4344818f6d05bfe4787dbb36f6861b425c91fb7dab9bd0d5dfa55f279acfd7fee3eac3c68201e97ef7d77eca0a81926b58b2853cec49bd39c5746f1dcddb756bd93918fc69ff97a5bc1a0d87322e81aed13dfe90a59d47d0ec8380c66453046f2f9e5af9e5e144fbdae3090504b8faab0c5ec251e027dbfb76d552bbb89c557334095ad32e3de609e57269ab85f0c93698cd6f2d2f6d0285ff4472e028a31f558675bdb0811781e545cb460d3696c5e49a7da3e38a616eb6edd74e08bb75a70dc90666dd62e24719e35b83c3322798d6c7a4c533fee584a7e3cab0d4ca268450bdf5a8c3bdb1b08a02f121c0696309b2fbce8b1f493764f60db7ea2d150a9c26cb66c535deda182b2095eaa0e9948ed825c7d1baadef27f80cb56997f2a826fb52f3b480e973aba44a8579a9bb99e52791a21ab922577083129e80bbeb4d58c9404997b5797f3e86382fdd1b4ccaa2c78037ca1d7621be6e480851b7ad47c1a18dd7b2d3f40c3486748e885b31c304b5264260dfb4ed1e08165df05742fc675aa8bf271671776b203d27916ab7ef21e7d476bb0a10c9d0478320799ee4d220fda1d4b4b7bcca123accaaf0325d6e0b836d81c5c22e41c3832c7df3a5f40436372ca3e8d6dbd49d8ffb32af286f3e30c9dbc0ff8957946680d06a48790a7b287b65130121d21136df9a917795d68971ec82215ee55553b17383e3fee334e04d24000065000000");
		Transaction _NamedIssuance = new Transaction("02000000000101f4733a0e18ee9d316e95a63cae43f5e8e76bc89df43a4c8cadbe7ee88eed9c381800008000fdffffff0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001000000003b9aca0001000000003b9aca00050a2d3339e5f5559dbbff9a34aa785132cef4093f039adcaa90a131d3bba178ec50085d9beb382bf30233c619fa526d88f63f0e17b649d709ddbd2e3c62411f7d692e033df4cca9e01fdb112f598ac80211abecd3fd4c191924ab43192e7b2c3353010b1976a91477d3955fdc35883fcb680b6f81e3ef2beaf68cd788ac0b88c13801835d26cd55f7e9db0e372437ac75aaceb1bff4b6cee57fb6a98ead1908ec193a169b18c681d988293d0067411e34e6b3910abfac9e8f12730982b919f80261b3900ca569c2045050c34a9b1a205705adc8ccd47a780b5bc7078fa92e3e611976a914a1e6028a0852095a42036a08545dceffab9b7c4488ac0121667c3dcc51290904a6a9eae27337e6ff5602d0deb5ca501f77be96de63f60901000000000000000000066a04746573740bca251918c36d5d16691e2b3af9c3ed0abdd3fe763b63293979e462c430d6d3a908826f416484f8e14d25b32be6065c2f5a4c36c473a44412f8ada4f08feae310690260a6075da990cd586653b7bb58af0009ee83c789e54cda8cae39e6d0a1c48eab1976a91477443c362559344c76a7fc7c6e7d29eeded4cd0988ac0121667c3dcc51290904a6a9eae27337e6ff5602d0deb5ca501f77be96de63f60901000000000000e27c000000000083030007500d5f662beffe960512855f4c89617e8d54633e7fe238b3596e6beee0403fa2e431d4196d3478268ca2aae525075cdd9d73ce2960df0035c2ca86fb8daeae02dc7532284bdf66ce8d2847e11ecf065f1362b8e475dd813444d5a9fb9022bf54a0b9ba62edf0aaae82d8ff8743209a89be9fe66efafa0bb77ea2e71af41bbc06fd0c0a601f00000000000000012117b95a5d0b60815580d230a77022e2089fddc40811d3931978f386c33388434492166562dec5db1bf1cfd06fac939e4115913940bd421f10df0405a2fd7f332179ae2d2050e86319982925b6d0190972c46834c1d2ef2e2bf897c1bef91fa2721e2ccde4c8778817bb3c2c5348bd53d74c39793b35aaf19dacd5439297fde37976ea1981543f84b5248a45b6502ce4675b53724c1de21d7444a8b5990897803c5591a86c2d363cea18d112ae407f1fbb5e4a62d7b915628d21d57dc4af9ca851a62529e5b870e746b34d16a91df819985aa146ae5244ebb451dec12f0e20f92aff739d144dd2a11ccf600e5b769c7ee8d81770b4abfd6f39bd03b5878ee9586951b37d67fec28925d1e99077d1e39b0ef9295bf5ca0a8b0c18ae796f78f1b9edaf35be4d48dc547a68043b5d69c430c0d73f3219bfac689af695aa5fdf9947ce0a4c8a018419937eb6dc501c82653a7604eca49d23a0d55679540a1b1502ddded242ad2007904812bb2c69048f01eba88f261382ac700259f04cf29952c3819ee5bf3692aa74f0c0ac71608c58ecffdd1a1a1f4144053b251a294639305c6955793b12bfa6447687757800f1c2b0a42e0938cc328c7f26e7c337f0736dcbc4e832a20f5885fb7077897816f6803262e8bed38eb3455d0d85fc501e780c144cd6e32e8b33c0a0bf4e56dd2e92ea6a00770c7e87fb16e8aa9c11d7cd312e15dfdbbff65bea991c49f049af0249e95e034ed4b314ab4128bf2268effaa3526874388eaf78254968f17c7f9c053a5c0e09f7bfc213fe57850f3d3f49c56ffe26d5c21a87f36f1cec84f8ce4fb6445e960b584a468b177c6fe7fc808ddf6982cf938df0048ebcbfd28c36e3848f1f1a56057abc8211f4919357e376007029f34cd1ff573fbc2250ec18dd1dd44cbdcbc0d56645ac08ff445adc79d9f064b352592d00a8ac45d078f3ef508d5ad64d2da66e5602123e692418985cc21ee27caec7db57b0ecd42463668c964e3871a8e9199a2834ff7038c4a82c9aa8fb6efaed938f7fde8e7ea0ac23eebcd5714ee8249ea970964e2099d5b4dd3b2c097f631d59b24f80bfeb9a292d79fe5e0a147ae8789ecc63f9a935e8bb061e41d4ae1ba95979152fa262323aaa5756c53f24552d839d7d96b853bbd4ac52272050ea2c5ffbd04392c955191911a5082021e22c81f1f8ae7ed515e12c045b3b31098dcf3ef16535ad47336a0867158f73c0fa2d69c5cea160ab4ec43fe6d40dea6f1ed0b959f1537d1042f0d9e150550c71adca7be2a3a9122fddb88e7cb1ba96ed00775729fb12885365bf637d46ac5bf3b7861dfd0d542a05b818484f928a85f51ae50da11274f9f983b9caf37bfb753abcde4403fc0db26253fe31eebe8a147484c81a0c5f68671b34e8bd4514f38b5eeb083fce7a192dd28be0c84e7aa034c4e87c8a68bf1ee0c2f60e4ec9f520e670c68ada04ce1b5f61d3951fd94f3d124540f3a0cc3075ea55c611c4ae92774cae2fc50c2c11965d59dced5684c7ab5cdf95069ef745a426a8bd0ac2a50b06e0abdddd7e14e36bbe923311a4f60a832a54ce6eea1e4e5bffd9ad545105dc514818143fc869e79e0ef7466ba791633e99ea20b810d128dd8526a6f216e96d1c3a64ffad649a3bc23da09360ce4cbd967e88d97553ff5116da532571bcfb22c9ba08dfd2b6d372b655fc3d95393ad77c2e3863988120bd1b03a222d506269036e882ebcd012b7cccd8a97bca8d9a0a6f38f211b958db229709216150cfc83185527b63214e885954ff0ca96c12b93b7d6bea23f1c1f9bac10fcd4e11d143cbf816b34a5e6bf9ee7249ba081deb4c8a3776fd56eb9d90069a63b5919be540d4cff3741fa9a77a6d958a0632f77c3389901340e992676836d84e82fd076b3deecb1b6ca6230907c21a57450b9e2e1c5f13d3190a609e64e3765753d990f2992cdf1d19be498ad2829980bb10aa0715adcccdd43b6185a3a15fdc35be81e3cc8f5b1119edd47b3d2d06718f3c33af7234c85733a5ba159c3e5651a58fdbe2fd8976e631897e7b4d42b168731ccc41c2f7f59d2fd0a06cd420927089fd3037f824753d431c4a9a00a350fd2c7caca5433f35aa1f1aff632b2e34ba526a45b6b53b56fd08e1f13d6b651c8e547404a758d16a6ce6c0a2e71428d84ed85c6c585a5872a607801aa2d76763984bf7769d871732b1d675adce0afb5ceacc23287c9c785eec3c07306ebad8e979a9bdc71ef571982d51244c124cf39615e695ad24dc0e8404fc42fb188caa177e8303454128e930159bb92db8ffd357799d2a1e5d598642aae53739a6ed5e254351edfac5572c96c4adfe93302b452581486429313fd5bb1cab57b165400f57b3ca66d64911f1d65281035b8de0475c1d97e6c6a81f85324937836fa53176eef74d027b942f4b823138508d25728af73a4bffe500f589ae7a30e64e34f1da8d189679e13e28bd778a322fe535d3f4f3f67c111429edf5a3dd05ae2de5270d696b275e77a0e49a347c0963d26ecd7250241e5ff541e480a1526f450765e1ed62a0f227b9e2f5eab1856473d9def424771bab74dca667d8510a416a76877c3ad151a70431d9c5647d89e1ac2495d4535e9988179de40519b5984bd519f8c2a96e407398f29476a7be77b2446529f4c3033f918e32bbac44da566111a2ef1ca6a60554bda5b30f15fae778fde9a38a0091a4b5af709197d89d92510f8889c1996dcd5b86fa8fc4964f8fd3cecc5ff26cea0dcc53bd41d43ed5834727c5a811ff2f87cfd06ea0de292fd5f6165239edf5365144d8211f76579781fc0b48e6e63af73534a448977586d59ace85e5e132a0c856f7560c4ea019ccc89438f82d166a5f5fc761e6e8ec2c797024fad4e2f679d99bc11995352a6ff61e878fdc5a56148f8ab4f05fb173876ae5a19022b8b552198f74b20253f29634581654c121adb9941dbfaf97b6682c8feb3e1ce9b0335f272c1cb19e7bfffa2e2679a8f67ba4e55f9280f96f969ff33e19549242acd6c5cbf2e90af26a4a47d0121faef759ff388bdafb84ca450dfb8eea8ba5fbd02b78a0e426217ef480749f3ecd3a42cf4bc06eba4df31d167bfe5d783ca7b27e12933bbe9ed6cabb7b2fafafe8bad10ca33995ff1fadddb3fdd8970e91b139f3d621ba2054e94547d0753f149bd09dbc3c4c25f45b7032426d682206baf263240402e697e70a562660d170362b9baf16a182c545c06754b863c4eb63cff60e4185afedb179cefc51beccdc4f1abb9b9496763cd23357500159ebf3d96fdc4052b3663219d6b4c5d15481ebe70c064d4b64303f85764718257e83aca82f0cad11af197a18bf4d3d80699e3dcb1ad02e341fb892002dc94ccc7a8e7c7d133b2e9d676bd37ca5c25e61761cc32ca235d841212eeca123f17def7356980f7d3b11cc16dbcd15ff7ee9804cb559acb289cf5d1c7e69130b47617edbb48fbaea66cc1426dbb680b4c6a34adda53c1b472d31b0c3ffb5d59eb7dcb85c4a7912018b967b4b1c2bdedfa93ca4fa15c7558b75395edf1ee319c0f71e5ad1e059aecef0e37d8b71f07b4b913e5cc420f4f8d6c40d9deddde0c31bf95a71fef51e23b9b3079d52877b4a830300074695cca6444d828d33b978fc508c262c42a9ab3a21dd1b8aa5c1380a03d42c6bac1a388d743e2db538fd2f53a3d3ba39bf7762ac7b0998fe23aa31e643b979d97b2a5758116a3c181c50eec951cd306a318ca75a0d5345240a142621ad0fa45a8acc8e340f3182d08f355868cd0217f280423b6040c61e674802487cf534832bfd2d0e602c00000000000000011c2f26c391145e3b9d9ae1a131bc21381597c77a94a06156b4dc673d142e7230ac7f9c1cae20d1b59c9c536af384534f14979c4f31785ba6837180b08c479399802d3f5c94e80e3d8df6b66624506f0ae17bbefe184a617e05aa031b06c1c9a70f7184b68c6e57d38edb3184eb2201e9f7aa9736797a47e91cb3e5be3f664ed8ee848aa920d4ff82c869ceba4579ecb59d5ba4d95b5df711451ef4cf2c86781a62a77b6e142d03edaba3bb69d863cc64498587993e0e3adab9f877d6aff7f3a3da4f5e59e66cfbd955fde5310cfc628e8694a1175d93202535b1d2ff5704f1739731c30589999a099561bc3d4b1e09fb0c1b12122611b4eea34b19f12d44af932a0a081922699f491e1a72ea1773cc719eca2179c9f7b8dc89a683b128c2030cc267b0df86f0da356be528cef7e6a640765cb9be0ae883a429c1ca5d72cddb18523a4f913509b05151711898e83b49afee6f68ad9fb3f35c34e88e1c311372ad2dcf56dfb6adbf94636c37118c1f7c9e95b2bb768e9ee676f4d07be239472d95fed8fc6da5f39cd4cc23b1f2962f843173fb9e1d1a2bd887e134bcb8177d9a5cfc4603b11fd18f51844f4c4f0a20e2662dbeeeb4bfe836bdb880df1b8b163a83d5d526cc0092e20ac61a79da1a734274c2d7b9f6549f52c8b5a262d97e022fe05b47a1720927d1691385956abe1f61e3236786e4f5158db0ecbb47aab1d4469ec6d2428eb4d77056ba09b8f7bfd4ef028cb0f1d09e5fdfb47d03f606a742f245bbc68b382e30d54ad0dc82a033051a2c640a3c148d490baa3b114c1ed1c7a86d64786c6a54dcddc09bd0a337f2115e5e9225a827cc75ca7b35addf7dd99d642a24d45d0aa9bcd5b4bde8d9eae445bbee9038cba64a874ec016735addec2694cc817e21501e4f27921e594209fe7d136e0cdab0f6c2dd0df407eb394f9e95a80fcb05c9783cbabe7afe519fbb72bee326405729d1381e363d30452840ba583eebca59717889aeba957c66ef9f35046b91970ea05079e565b15db662a9507468dcff0333489f20e2e9628df8efe68bd45069e11b8436290df86cf6f89dd2e44c5e5bea23a9044f496249b6ea9e59b62d7f70d905d6978399b22b60dfdfcbee9cd916cca842ab11f46fcc485fa78d4145e73bd0289f9f994f45406f0f17821e91d2920acb4aafe10bad5afa424544549af11c7e5d374db93583b95bc37a76999f6f21aa00b2222ad18bca70faa9f95eee71e407a05bde03870617c71699a2201ee6192d9b8be592c9df6caa5d487a05c00208ce488342cdb2ce2f7d9a0022d43ccbfd50074e6c47351dbd7b7ce1d1daf2ca740cff542df3edd02d243d91728fd7dc37d2f4e5ff977981193aea9ce357bf4ab48035661719340ed7079f7bbc7363b87227077a5782b126e75811608490c96f57424c93a82865e3e3d5a6d165708b251a31698f13d949e6b618fbb621b1b74e803f4df2567a34bcded7b2903853cbefe07ea2bd1ffc1231d0093b7927cd0fabfbe7c136f76303e934978af84c5d74a14fad034636f484eee20525d4da014cc6bde5d022bb858ecf3f268afc481d86ddcb1021f16b4cdc6fefa758b9839153b5f8beab42918d0fdf122da60339688b55c3a27ed44640cd9738c45a533790af553f8057b142c7502ab63e29969365b877235649b22f76f73fb75edfcdff4130a64b1d6c3107d5c5991d1059a667b782fc147fe3a8d516876cf1d6dc575def66e52668c2a089c277c6f8ff21df581b7a1bb3ed22c654a9371b2d7cd52232d6143bf1ccfe113a7d6a744c98bc25804f5a7e055048e792ad200894d26130cf79f118ed6919f52fb503cec8e6e745d56c64a297bf61786132e8db4c71ab39e2a0daca39132171b2b719999cc3853e9ba3ef096983f78731fe7891605423e6eb23fa55881b56b8c8d38877f46436a0eebee7d584134b5d34625d1d910e6c7783ba9c82c0edf5d3c14304ff5d361ef9cf73ec178b43cef66850dbf2b7b89100cc3a183ec2ec3ac3ef5426a579dfddae032ceb0dd85efc8e6c697ccd95890ff10c973e14a79a59adcebbfaa31170a8b5b51f75303ac5d0ecb64da2ee1209410bfea8aa4be024600e6e5a3c8e193b0e4ae800ca1326e8bb20487c5542c153cf0719df4a47a5fcee22f6df7a9b506fa819ac806854d3e563ad879998ededaa509a345892a4b0671409442482f561a7b8ad5aab887d32f131a5e037088a860a76e627fdb659272e795d8bc4183c54cb40aad0c04683140c61b18f2aa69f0903dd9c4b98e4a001e098b6c04b1d2b2ce1a9306342e494ff03035cb3dcf113328b6e3e100e8069bc0371d92ba099942e10160f85ea71d82d85d743f92f0c6c300554aec2c4e9d0f6befc0b2ec1e77a35f90cc5f00fec0f62666268141d564a99ff4f4e2e30d8b1caeace6cad48d01a0759be43f143539208b5ce379ed18be23d26154f3246171aaeae657ac91ff69e23bfe719538a9ce92b326bf32201a9a958264dbf797bb0d346dbb366437011abb838b2cfb92e4ec0b72a2429970533e6c361ef3c34f5830a85aeb6aee93f39676b3186c41137c03505f4ece71dacff7ae18cb0a9ccd405131af88bb538dd12b2389271359ec451b61fc206c75ce8b2e4307cbcbac1ff55267cc53adc3a65b2ea212abd72105b7b5a770c922196063c6997039e9bf7d9e1574790fe75defe2a8ce7024adc49f9c2df6b7ee916e0b73fffbd174416c25551199e6f2c8d3d7a73877e7c56515067234cc50eecbd7fed9d165a6c86a12c27f6fb4ca09eea70dcf2d39e6c09056b1d731d2ca6ab7a7d324f6e77c4dd8efc27f9ced5edc74925fc71763ee1addf2066e8448d34ad702ef17fa8bb00cb0d5d9f3fb62881886d0311802827a5ea64638faf8fbace8f618af2591a8f9926897d532be3693c7edafaa5959ae483bab4f8a935649b87f18eb14659848b404f9bd58207d8cdab4b12bca8ae2884c312fb8a4e2822e2febe9c2819f3609b9910748b97f7c8e9116e6c6460ef2ef7f7fbdf87fb7f1b15268f2437ce251904458e7c469baaea1efd943b9418b9e67375d8d3f55f8caa6782669362001fc3cae37a78d8ffcce240a1be7b85ab0b574c54a2087ad5cc9c421f6b8990833ff20c918ae327c95c35b8c725f508f75dec2aa481f586951e46fc37640886662792bdd894a8d4610ee42e4854c2a07a2c8a0a786c5e5ce723339685ab9da2f749591e45a95d6c086c0ffc871d2c8367dea3078d264f54c47fb6d8ba404cd35b6e0ff980d6c1e9f401495a72d592b3410de891779320b9275253bd1b08bff77832f60073ed20d37a4e910f323896e9da6989c8b3b2583f3cf161e77a9216a23ecf4ed2f36f8e57d4d69238542d114569cdc15c264f183bc7eacc5ddfb3783d1606913a1cceec6784f3b24a7a48ba87c97917aed3b791b6fd5d593fdfadcebd79af79c3de7a2aa50892f0bd2764d668f45cac7539178953cf2a6c022c46e8bb0c7a468d0b654000a039dc363e4a549dd9a0f27f8655aa88f15fe032f55cb623f211b3f288886a75444266cd974e814d1b422540002d68365423087b27de3930938feb9988eacaab895148b0f1863389613a41fa0096616f9533544b6b50cf030f88c894c6e11e3af21520d73805ce48166068036936e3df3b8ae8f5ba913d3e785e4c9b04bee464540b307982b9f0e840e2564635d282da33e94534a4ac2bd72b122372c38b4f0ab5b32ffdfff4ae58d3abd6f204f82b8e41152a281111282b55c374f2f5e409d53a1f54cc240e56c43fc9deb172468784160d13cbc12863f2e53daaa0685c6927efab4b33855522b6b9b7113de562d78dc0f2951cba86d58cfe7d88b7e356173b73ef7a569a270ba68902495c3eeaea09d4e26a09d9479e9a0d040b8634be4f6c03ec2888d6da298430e009e6a3895a9fe74ebd2a9f7a7d5371aefccb9e2c989c642c692f3178dccadd98f97c4d6ad27c0816d6296752df82d28ceb298d79762bf2464dc365df6a586d98291cb8f3809aae998d26ecaa358a896f22f1603d4d2f86181ef120cd82302073bab1373d42e26e83c4fa6e1ad0bffb886b59e0a89d71682bae3aacc659bae654df5643f2101bfbbaba6ba375fe8f9f5d7b07752649d35398379d828c4f2dfdd8b98cdc79b7c5489478b9b7e1d6d98253ab0a984796135de83d3847e71bc7c0a7216a9324fa1355ff2171dce087fc800083a7ef1ef8654131e8c6f264d75b5da90c4b6114f147b60ebcafbe328f0b0f15dc4711dc3cc1a328852ab39f9f3d364c274af27b40725a746004717fd572e756abdfe7a0653dc226e8aec152acdaac1222f9047000e147800e5466eb9a7ed6d630b200ddb5e71967d06d6703af32cc8531d90a17a8e54ace1237e1c5966c8acdedf4c37488e7e8c89474d1d5440f68b67c840e8b9320713025da5cdf7ddd3a5fdf89deec9fa99658386f780b824c30127fdb2565d46a74adf8fa4de65ddfc523790ee50d328d5e4c8c0dc33be87ca5a4e9b9d01e88a180201895e9ee9dc1254180ed0c03da5e67b76b4edab11a208614add3e1023b33b6652ae8f3700e89223f8b46ce88e156463c64b7c29488c0eed353f52c61954885cec3c802eeeb0c0cb67da3868075d411faa23060d6539deba785d48249203bc90d7ed899c17e2c04ce98dc25031e1e5d1acf900227ad9377505f12040aaf6afa3263b0a32663da1246591374c8ea4bab08760fb281c8e72ec3737c79c9f6f0638bb76f0a79fc4fc24aa445a4f671755866c99b077a16aa92071ea1f3c5b6ca239bcdb3d3a80cb8ed47dde80250b644aea35003a8a0dcf544afa5a41df6c58aaf16365035dd5f73c1f303ad904ce5f68f989cf55342a3bdc4ad16f058be68a31cd2c562f86d9c3705f1a08d3e83f5a4c86d7d03eabff18d73e2affad7ff628aa806be15eedcaaa791fb33f26433fc2f0569dafcfc3dd8739031d644070f368517ce8a023a130c3870e0b294e705f1bb6a35459986423d5b06cfb38652e251ee8ee8ff3b7b0e0fb0c437456c1743c763c28d9e9693e072e7f877066cc7413500db215b0d05f78dfd5964cff5126e0450fa4145fb4708f4bf1fdf374b064c3e808250eb1e0b1b00008303000769a5962fc6da46992923a372e59c4e54b7b5cc9c2965e49b6d0e68f0f660085273610afd204d3d2c355f6c61dd990d1bee4321ee279230921abecf43cc5256f66e931631dc411f6bba5df40a9518ec5bf9836f9fd4582fa47b98debdd82b8584944a28bbbfa81eb04ec9effa5699a00b45fc4cff1e8c43587a008bdf10601be6fd0c0a601f000000000000000126271f5d807a1fb138e03f06c4a6362a93f32c306bad24a37c6cfeff0e87d1b838cab69c99c11f77750c06e7ab6f0b7a017cac0def99dd342c5794d0808fe84e04b8cd2c06355cd9d01449515d7e5b2d57aa134aade11e32bf380608f09bf99de50c2a3d4319af5de042c5f1137f03def98d953948764c6b8460f53eaa0727b2a1b9064655e73cfbccc36bd2d814044af969662f64ac03eeffd35b31800e88503791fa330d9f86403534fada7a3bb75b637a9385c7587a0f7218dc463fce48bf4b14e769eb40a5af6bda9bac1f6f098cdbf0609cfbe331e3a3be262b7878f75a6a471280e50c09b4989457d978631c3f8643981fcc6805ed0d12bbf36ec23d0cb13cf549ad576a517fa86630fec1b32c2db30d316d1bc620959ee13a8a5f445db91baca8943b685cf38bc5158e8b03ca323ddd7f1a78e6916d5685cccad57d9a0d1cdedbb5819d4ad89be7f01a920112d4f57d7dde55f63aaa9ef7f4915ae74d68b1a62355d21ecf05b6a3a650d8ebb58bed953b4c71ad57056827a69f7891a9fd6b5d91be70e0231bdfb876dce1737535a9e1f3d055764b59d30322a27205f89bd9bdac7eb74f751b7aeaa2db785f02fe26d104e36dfe0edc2bc5e08dd9097c485ecb6b96d0316ad958798d58cc9c4e6089a9c84714686594f874f7e8eff137ef16c502ac5e494f692e5b31c422059e9fa834a86cbc1a2e66722fbf49056098250f96fb0a696bfcf3c2c2f2f2f7cc16d3f12748f8a80e28f7ae0c90448d426a48a4db427a7773399f4fe8a0a8cae6efe46082a5044447db06355d6fbcd496d51176580c39d3ffc24e90cb2e44a64761a97988fd3811185f05202f6d54a988a1d100df0f078f8cf793ba575d8c4dc5d4da211b5dbb3505718b50a5410452d9325afcdc290c22699e9a4b415f8b3b2ee7dcc4b89a897da87af2bbbcc92f2f4216777fd9fb9e3418c0ae3305caf244619bdeba3494b15aeaa75ccd895acab30d02e6b7be5916a1d27743ad367770cde5d2f41494b4fee3a19a0b872bc413631ca34cfe4c26e54179f77f40f8caf36bf98f8556813ccf083c98758cfa3f773fdf78be487d6ff5f1a70302fc4fa813890c563535a64b414e8174d5cedcf375b756618a1aac110aa0075e45dd7217dd9eee6af3985a5e971bcbf542d45ed24edc71c13266c002159294e451dffcbf6134ceae742b267d90ec395f7d5fefd44dfb55392cb0996dde1d228b0c3f0d15931a697084b8a7d1cf29b383cdb4545fe89d5567ce7f928af29059a3ebe3131cc2f029be0a509a594ac9ffaf44d6bc46c5742e3282f7e33e34801364e04bea0de07f44596a1b4b86400825983d763538e14bc9bda1554ffe50c74f63c329f81bbb9e9ae3204df70d4a899513cca871f3fe99a47b4c560eba05d3102d6258d20e156ff197f73c43238bdde5f136f5c619c4ac9f6d95352ab5ba63b9b303608253221e6073231436fbfb61c3c20a980d47fd9d5078f659a39296c8c7134ab68b917aff298cc33ebf80a0b301973a7360da4ad2235020093576579f40bb9e3b96f639529e84ad1295af6dbc5fbe8a7e1d7f5cacec4e55b94abe86f6c09378fa571ea027d26e433539e45cd1dfd02b6393e42f99701272517d9768bb736c2bfb6f13b2d56c6a3978d35719d3cd15d63b78ab7a72fdb9007fbc44c4848eed543777b2c34ab8cea56ad1d2ed16f988ee028e0715777597e29631d365a43219a33929fd49f477ff2334f9c8ffae88301670cef6fee0964473feecf1f1a10c528a9de4bfadbe022bb17ac1873ca25ab8dbc3302967c63c400c2ccf7bfe0e3ed56232a6b553fa4b584846d9c2b33e0d884b0238c687c3a1314f51639d0a72dedec8c22ffefd13047ae836df0d26ae4095cc2687122b8719d190fc7a78c678caea0b54b052f22c0f4bbac6f9358dd11eb23e1ac930c7662f20381542c68f276a82234c2e6970519d2e6dd9e80026c2699589fac0a7f699306ebcbd460bc4acd88c3c895b544b764f9ac734356a62453dfded3532be3dcc1f3151673c5fab142138a3b03178f94944cb9d5f7baed7408473e90c395ab86fe1f19666c9b289300b3d2fa8e58cf430594ee834cd0089c849cf2a8a64670b6ee803b6bad2e1d56dd85c9ce3a85693c9af2ead8359d15dd2f7d9978bc5d291980d7e0da8f439b77768ea291b99244bf9f70ab35bb3916cf8ced0d3dbe4d8488e0599308e911395e9b0e243a5e0b121e5ccf75092e67cedde92e34f7966f1b3f0a539c379367dc0cb80deaedf067ab8f213d1ca217800ca891f7446f460f60058f8512cd06937cb216cf9ff516007cb728115aa02c3daef60d850d04c12a629bbd6ceb2fe3a78af96dd53012da664e606fcc67faad8f2502cef4b40cbd6b3468190cd6422e13ed7ab782b440757a6419f25626136f5c90f6ef05bd7ec63d9bb366aa0880f649119551cb2656017a75d7faa1f73c15e9eeaf7bc5c2c8b4b3b255f0444fc0760b3debe4f76d51304591d14a1e8e285fdd1e59d022955ca907fd8b246f6577ac218f098191958b047a41330af088c162e973a8bb4f356c242f373893442476dc4d6cee868a1f6dd80fd165e77c2731d347000f47d402e8e8b93df498f3464d92f8959f7f60127613825aa50ce51509a33812468587a283036ae3262c0c3d525985ce3a2623775f0f88bc6624dad18e781151abb291ba4d2be8674562cc77eedc7985e6378e78a534a6c14a1302b9c127a2dc12be3f4dd845839cb1e83d94a211e774eb7f5000f3ef5860d457a3f61fd9ebc020895adfba78b524a7dca803d5f69ff013d6837e86fc37092e9ef82bb02fcc9fa2782e089fbe877a292820108976f086eb84510f02c7bd133d9b2b078014c47029ad19d5c568f27eca6ebb4dd64874d6ffe58b1c0be94f486530dde12ee06ac9d1a5d8a2686806151c7e4b2dbc37f078d4db2003d4cacd141d852614560e53814e429a51ae92d63f6133011c1eee61ac72165fa2be6b577368d9c54435e6bc3bf9763232edd2d78573a1943df54c10b98640bd92c8d209a426d1ce2b1af7f2be5cb4deab7a3b49b832d26b489f7798772558aef421e615b90ef794d25b438ed46874cfbfdb10d4139f71222eb6f09e31f629613e8a8a0a54d297e7813eb849321e52c82926846396f9e08cf325a32c4ef55e0da989941a4828172ba5dfb925c20cae3792788875401b9348836cb44f141b758af12b97141d948449f7ac617f7b181314788aa1d2f5b49cd79656054c56c5f4fef7c0ad1bfbbccdf42bec69c664739230557c36ef35ff252211bb9cf39f3b827b23aa785eda9524f8ca5ef6922bda068d891642a55bd31e3cbf4bb33998f127f61db8da6268d7242e7c53d844a0fb1e29c9a8f4e42b9b335e2b511d22987c7527886c0a7527032a5dbf0c5bac20574bc7d15ee96b951d59bbbb7b473e993ebf9bb69524e6565165e5210425bdd06a9c3472f87e858654cdafcade47ccdde615c7655c2860a00e67de75f94cc51c0967f23291927389d934cacae89db65e41c4429336ae060ce49651c92bf7546b785798aa90d004c33e375dc72aef1f0c43ce0fe22d4e163ad8858594ed6704e223c90cad2f3692285f72cd7c038000070000000");
		Transaction _NamedIssuanceKanji = new Transaction("02000000000101f4733a0e18ee9d316e95a63cae43f5e8e76bc89df43a4c8cadbe7ee88eed9c381900008000fdffffff0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001000000003b9aca0001000000003b9aca00050be3c73d2c686d7725c7b41348232f3309038ceab8533e6db2ded695e2e5dacb9e080d6cd905017ea2027e637dd119ef928440ef2debdffa1e98d11b6fa25ba4e9f4023ff69fea85c87507adfc9ee53ce4c81e44d05e89b6d7592fe8811d321c8ae1e21976a914226a65d07c9adf0877996d21c718af36fe1c856a88ac0121667c3dcc51290904a6a9eae27337e6ff5602d0deb5ca501f77be96de63f60901000000000000000000056a033f3f3f0ba57d13e7fa24802ac603a2d0879a98ab9f7f1146671ccbd81a6e7f40ece64c96080c4db8fe4cd483201c1344ea432e364592af465e755a609703a9e95b1502bdb20361fed00cbfb6bd1fdf7a8b38ddfb89e8e26eb7ed73b7142e6bddb9daa12d88711976a9148bc49cf507624bf763922c4d7bd85c6139c445ce88ac0a51b97aabc9edf57d2628cbd1e29d7f391109124b807d5d13791f68d4c06a9d1509f4175d562802a9454b42a21f573e94af8e71d05e0de1b6d37bac11b8ad10928a024915f17ee10961bc2d5ca9b4c5a901780659ce7c2346d17584dc18051c351dbd1976a9148dfde3cd8496ef758c6eb8bad949979010b8c23888ac0121667c3dcc51290904a6a9eae27337e6ff5602d0deb5ca501f77be96de63f60901000000000000e268000000000083030007e663fa26dc59e7bc20cfea7ee7dac3a4044be2ca1c89469300ade76f91b72ca8fee0d11af1f35833888e18fbdccd3e73f6976bff63e34aa335e2a3f037df687ae1c87e43d42b84615fe177c4eef8d1fd10a9983f794feb8e6ae76de2b11ec1e3b3892f55878bf842f203d75b69702d89d9878ccb35fc99adc225b61953339874fd0c0a601f00000000000000018b3e0cbcf755051f20a0aa90fb6b61da1d074e7e88a798ce7b461eb88d85b9d52ecff04338ea20a1c6a08d15ec16c335bb054d64d69c92985eacb60b50c148b9d472c2079db2b7cccc5305e8e41455f901f1b94c38652c7e399b6be7645f48e3016d20aa15f9352b2a8f868e72ce3238c668e66fb28bedc72a191caa8183ec2da5bec503159b485fc29d6ccf46cba543ae23dc6a6ff6c4f58a4064118601ad8a38b73e579fe1716b6969ae3dea09b6f9f78ff1d7341836e91b0ae9cfd7af1bd27e68a239f0349b6db4cf1033ec1d90f51d5ab1ba6991165f452434b5518c3201eb879b49bd7b813294a03854c44f41ad617e211531f4b33809cfb05031baecc4359adfd79e8abb403cba534346c7d58715df68968093efbce8f01c894ed97c74c622b6e8349a9e24c5f9429e5c832d6e2af1ef49f3cd6db4ef25cc062f0bd59589495702ba9f277790710874826cd9f22b034db2f902e378b56a007bb5c80e5470e9b6eeefafaf5a1724ebbb1d04bd9d8a2ba417fc21ebf6ddcf807ecdc4e58b136ea752e0affc5ae2d9f62db22c7dc6c64bd41db7bffeeb8ebb5d2ecdaa9467bf257f8a27c07a58b5b184bd812a5999fe992463e24a7480eaa821b33597ac7717f05f9862b9789f82bb8168e414ada249b481c7591d8c4e0466c04d65e7172e22bc89623cefabe258b293d7c882b7f425dd404c944f82ddb81b6ec7418895a2c68988cf0fea569bece7654ec111b010f8212e0d0c81ce0954ad107ab9cb7f7e1bfdb8461c4f8d32e412db73054b52828ec4332ff334da8beb191a00487be7607b171267c2591ad64cf116d12a58e09ae24a05ebcae6e6bba20e416e2ca1d3d6516bb0df3b9bf1cd938c9222190c2fb016822c98c80b128325238c577875a9b13a54cb1db8e6e724f2612308f8621aca74e11b4e3ecd7c09593cd46b68a7eb3fc1cf6f0beb8fd31b105c05b33ad7e6bcf4947a68b3a0f9f74431f3bbed3ca7bd874e7e55a06fc1ac3207176ceb6ab234f9e8d949a1132516d50a0faa89339cec0d0e422d1381c021895add50f2bf5920390b5682052a52e241ff50735a4a86f36f26bf2e05fb4c2ba235979ca1d1896230db32a1700c51562dd39984bcdb1bffad4e689bf8a3a1ef8bb3026ced638d49104788aec6953755122c464a7cd3d6635f9e0326026103fd513ceb25c68670379ac11ec344f105359bc92e6eaa8ec1110159c3800d4d7521e50b36ba3860fbc294d827676831376610619d2972c4e91efd2211bba79f3dc5280cc5e8e089cbe7cd1dd0e8fa4721347f6eb810966990c1ae62b32d925591a275756aeb3106dc3da02d163aeefc75d03538e543e66a1604df45adee87c1396ec706788394305ef7f492fb6ce2f15cb77340ec03aa7fa27eec04512989804e35aad48b9dbcddbfc7f85c0b4c9213c8ad2742e421d5d279a7cfae61050242a56bc6eacbd73e7273537a0dbb29ea45875667923061d0e4657c4613ac6e6c4eb4153a73deb0c769295ca8e1fc78f5baf301b3a2dbcdd4fb6af776d20e80176bce5768c3c5ed6bf015a59306a7b83d481eada05344ed5ef02a16136757ab119306df3b45e3dd915a30ea4ba6ccff902f8ff2bac3785ba9806490ca6def2029d9f16084712b3f6d0615601701ae5146bd71c9a0206a747979ba1693886ef009856c5ab4bcd4e4a4997b8d736368b081ddee4f90a5e799a453544ec5f49d72552ebeeef7c2ff94e7549e7004a064f677a45ee9487b3892fe043abee80effaa3c3c173cf756382b0a3346f57b7b302266b08b0cfc1d72640182ea8355f6ee0ee211483835c7f7267aab0d751e81d60b8f8ef4b4018bbb21ae0f94ecde8beae5293dddd137e15f3323ec716220bcaeed80118e54115f7d6c5ccf58522197fe8f7f9f3577f69945cf072f3ba7bbf4afe9f296242e0afe568b56642099a267f27f816a88b88e081a45e10fa89fb7c23de3cf36d90c9449d77c433b9a462b922adabf5b8b04d337581f1edabd5f48c795b4ec7a537f3432d4942e3907342e07c1f532f2141adfa4fffee32837f924ef7a9e280c746490194423ae7144d0c5ba18f786f1234794996c8d39c4c25ee3e5bfa1f1ab4e7617b25335aea1d3a0f7cea28cc97646925ecce9f3b663dee0849d3fcf07ada30af0e84acfd4d5a422c93acd777f9d9bb6211b4ece6d8b09d589e5966935808e5f77efadcad2bb5f80cc7824ce01946e973a732b2ee747d6f3a090b5372f62ab913240b7bae8e64309b39813a2b8d4beb2665d7f571c6bbf63e88f0d195462d8468abae84011f0882f55134cd2e3687decbb78cf69f16bc1f51aaa651c2d4eaef86a099f8002ba1a7f79b6d790394ec2b22e5e23a743a2ee0ffe01b12f7171ef00278574fd8d954f63039ebfb4fc14354452774b49bce9cc7b4434ba39eb77c4b896bbb2a5558fc190e6e7d60a91cdb119576977de8aa4255a285d17b1f937baee0d51308b77d7dea330d0c4d71adbc65ab72dd1a14e594eda913f722f53570846cbfa07259c3ad9d9f29372c52cfc3a3dc1181f5117421784bd08099fb5685eac54a2e437a195d1d1956dd343885503ee75a3e8a0eeda731254ce2443f426c65082004b0810cfe6aa3397e222b0b2acca03a34ecd0b7863332c47e537c22f212979d00353be960259b9e553f32c0f7dcb6100232538ca91bbe58c4a60a601d8c7e6825a6e4a6afe5564c1f588bccd86ce5dbc17cd8166f0be1873a15105ceffb0173b61155ef198586a32adbcf08f41c24a2a9cebe41b5484437d7263f51d883c93b51106ff79d10114c6e195405038189714318201118b92bc9d8b48eced7bebb9eed14808b1236f5715ef72db02234df4149f2029be7ae006b5710d654bb66a572e437c1479358fdefc969403aa08ae93047c34556db120e246f7d4a5856d031b8a28aaa1d45c2df39da44bef5064df7e4f99051cd623acd0e30b6417139ec3300147dc2c89d771d92d05e881a568b5dba68d9270bbbe7728b797e022eea5000310f0ba39e8bea49f8b02e34ea560328fd14644d6e4457f95d11604dc78950db40f12657ee35b87e09b1b15a7d2b1dc1612e6a16a57049aa5ee56d654a0bc853ed2a08dd77c7821097eafeade6147681a335c1f580e2c1ee0feed038940b177689a50cb7f2b34be7d89f339f5761d8c43baf70d2d677a5a1217ab9e73f85469d2235b8a04049008188470b3b236f740cdb29aca81922aa0cf02a5cd304795e7ec6c2df0f6ecd6082bbeff5f0ab414558ced7ee2b65a87b70ca2767031633beb3f9a21fefefd30e0dd981d9302e46b6d7533eb356fe7861d24fcd4c49613f0586aab43a28f1b63d365b34e0eb084fd68539b041479a83e7b7d067365896e56f06b58f7094c05e43935c3db3145a368458b1d3b2f66dd6f1ce6ab6d9a4294a12727818f54afe136c4e44d29d94b932d8de47db78d45b1bb0cc7b39b9805966653d389a62d00ee8e7e09b985ebd822dffade323ed6ca37605469a89aebd13209d538ae96c431ccdf3ee7b86c8036841a83358ec94b2d888021b9406a372a780751481a82e1aeee2fc0825f0976040fd68e79a34658cf947dec773137041da064bdd0aee8952e3e3afa65f8000083030007f40f1d9f8ff8a97ee3a5a9f3279e699cb0232f974dc8863bb6ddfd82f67e2eb3336e436b74be33207a036cd89f94f424e8534e638aa841392e429124bfe1d0200e78d5364efda531bef38ceea75eb9e4ccffd436e09db9bdef9a97e6c32b3673f8f9a71400a8aa16e0dc93bd94e262e7bdc6e9a3b463f66c731f738eaf046fc2fd0c0a601f000000000000000136114eb579f37d2e6faa24963196573e23fe27cf47931decaf1a9920cd93acc0ad9dc4fe0aed6040e3cf61d5d03dce2be3a7435bdefe67e32fb73dd70f9e9170c4f2fd6d8a0879b73b821552b20ffdbaf7569f6399300ebef43d462184db52e3e794e5629bd7a14c84a9d13dbbddf974c55293b40ab0b066e20cc0956536155d9a8fe2df2be37cf01a7a211a51bd7b38673b21f1f1794c6a88aaef92025339e4e745889df08af8410d16126f20222fa9c2b1864aba90d87c3589b91e8b87b54d921d1930b27b566c2e2227fca8a43db4cc931101c8a70efdd37e965e40e322f338d1d1ec4e66ee621ee41da93410344707a0f7757cbdd03180376211b9d080a8ba34565a309b7c59af4f899e8684c90a3edb8f9f3e69a8e85aee8b2d677d19d9704beb9f570d62259411c882bc6c5692a109baaa17ead96700a2cc1a1f98bcc10caa39e43f24fc8e8b2a5f235a3a43209691a490e80c673ed1a65507d1357c070f36dbccaae1b32ec2b65d642a1c49845bca8541d1b7bcd725ea23fa50b8b2e712ea7edb7eec90a26c6b2ef30a5f22c7b80aa70cffb77f85810a283e6ae319cbd99194d4e14e837ac04492a01ef32ce97c11631d8c6b7f0e7b00ad72a8486dccf98043914fd7111c64f8c8c2fd01e50f70eb5fc8d214fb7233aa27fef6835674ed9992269f1e2a9f78f59f156ddcd4b629951687ad666557f692c5baaec35e59fb21dea9fb2dbd0deeb77c596de3c6cdcfc6a55b476775461a8a91fdd804a2e6fcfafb2c2889bc6005f889e53cf0d9486da4e4dbc1c86888c90497db6ddd5af0745e51a63c39cfbfb43909fea22dbd26e6d8f3a11b9ef3d192fd0c011838c0f0b35b2a7129d16604d063d461c3e6a6d82a84492b59bbdbcfa3895992bdb5ccbb14926cb2d6ee4eb842c17077653cbaaadfd972fc9dadbd80ebfa3c8f297cb696baed06d0f94081b25839be7c4a1e4fe7e4cafb6ac1edcea6ae07fd277938b52e4ee822f26a155737352fbd2f95a9025f49a00bc42d4635ab694c116b6ca39e6f33230a81725aa74a63da4ca0c0ed359a6aaba46c48dba273b574e47bf1234715e6117f7a98a9ddd5b7920875f62d827e06dc2722570e90168b45ad8c998972d0e661ea83884ace82197f889ee6b92a017feff684ecf9cf019450fb59e3560fca2ad9c34d3bef8ca28f0ffc4bf7ddfda0a74bfbd0022a0938be69e1fb4ab9ec01d01697fe5a3c76da38aaafd46270473e6f33cf3c552462b0526540a99fc38407d5e604c5d9c4fbc9f0eb8dd89bcafc2a278f63ffa96cfc66c037f0cf985665016208ce505c0879ddd27fd4dd393f1ca5c7526fe41afd9d9769bbf4d755911df88dcbfba62c7162dab9b411b38b0f924c11ffa83244223b28dcb2ae8c4bdc4dea7a0f03e59a0d75759fc55df6a4dd9bb1bda9b0c7ddb25a8471d7b944b1cae2a229c6a7620add83e09da1e57ca4412e8b84662ec879f175e2ba8071e2ee13975dcd99c2911a466457777bf0d9b7b9110407df1aaefb61c290fe44f426a13200b2517cee80f6ab70d3fb1f5295e488ddd40d270ce545186e2efa663144aa340398c31f756d854147b51a2ad8b4a3ce22716b3f41782af9bcb23552812e0c677b67e20f4fee76396be7d940980e8b5bf07c96cf66a86a37efea61886a2acc21118af8a59af9864a4729a3cda6bdff6ffa1ac84396c2254d4b760232501f51f89152e30b11471265e463191416660580e76bef85b33578340de5f0eea66a05d1ec89cc2e6325f91c9ae1dbec83fbccfdedfcde9841d863764e125c91ee8eb3b9a115e241050b2177bf06d4e1fef2726efda5fc5e9f2154462ec3a9ccad46ae8c51217680d55b12c1aeff6b5868c727f7369e43b76e4d0db103b63a929990a17a20a8993d2bd762ba7d0a5dcd292098f0b6f6c7c0b66791128beccb6a8173ffb2812080e5e3632f23228517671e4023ef169aee8bc7a63fbc965dff02ee5927eb26ccb470eabc109117819e1b4981eda63b5bdf746a81fdff1b186514abcf95a4cf5200cbc983730364f24afef9ec88633ad80aff2d9ea53963e7c3029e06468ef4a775cb9e700314f2ea85e815ae4cfd130e7488142556507d368fe276de5ea6ce6b4d706d7e6fb16725f68ab5a9e2c98b58d1df9af7ea26a50c2b3e285138485411c8d87854564bf2493142e702c94c5227e3b1c8eff22056d760e113dc5681e468e3229223877dd3c32ebe44e4274b290212fc82de50e2f45410aafe75d6d0f7250fb7e28335bacfad5b2ec964de730b060a75abf73675e70f2f7f2083f923ce86689217b1c5efdf5833ad1b4dff049d47ecaa6a45477bfce753bda856e9560d99c6557be432c36361fbda37ee0b566a2fedd51f577592480b98aae32474226d537fdf15c349de7d88169a912c9a92b0687a3a3cb71348b94bae4c7a2ce8f39fe2defad05773402c05fe362bef531d4f0935a4b61fe89c2307e87350a41bceab57a8076f3b9113cf7026d74051aa3909f2969d565cdb0286cba34e31e51c7a1043e357799450e71b8b42f9e6444833f450a2f5a1843ba22756790b9bddc71ee9536007d7dc6e9233e3e5e4a7903b821c545d4b2f90b01e276a7fabaca7b36e5410f128e4156079bc34b0f4034621bfec153306e7d6a3f8fc6b991e92529e1674092517f9514c60abedf9047bec2069eeaa8ed6f6b5aa596074604f60b38c377a54d3b04e45cc9c2130f1727577768746705fa1db99b592badc82526679697a2063dcd70a9c43b79327a7da90266efea632f70b879e51014fbf6afff2ff2d5d7ed11ed1b915a1d67062f77ff6592b48e150b0b03dcb487a61aa4a963f52dfbfd422621fc2efa1ac92e10ae1d7e57d08e4a0971838f39ead3bdd424db46fe61fd9b5b6486a6ea2f24f4640629444db5dfae1b752c7f03f01fcc0197ed0f0cbfea9806a1c490607f1964774a3140ba5b4f05f0640fececfbc64b3e2d198f031cc07e598686718d5a823a2849ff6a7e140c39e11c72805bb66c0caaf0c1838faf47fddaa75f0c5c298e741d38b16ae55980a08652ef81b40d4e92508551dda3598c94375023d4754e3c240cfca87bc343a492621531cc5e9260e82dc4e34f92b9ef774417bb089577062b6892dd6670df15313b4618bfffbc3d1e874f5c55ea384522bc7a9d3fefb9751f8a0dd159ff7fbf7f05cc9537fb4012b182a20a9ac31917aedfcd1cb1648895e59504d992eda5659866fb6b633fb659e6b03320e5e1264df9f10b8225cc021059658414e20b990cadf608aa4967d7c395d4a8556df991d7bae511b39d98d7364e02a7adc16f694b3abf2031e190275d369df3c8cf535f33e8a6ac6574c102e22174ed5d0fa79675b4c2e26bdfd44e47b53540121fdec81076762651c2f710b0330a01640af21e06d34d9acf6b67dc0abd30e34cf6cf936670b15e66b7c28a4f08b7e77f39c7c1bae6b3be06f6637c213735cd884c288414b84f07b5501c263d79409e60dc031eec5f03e1af2fc8962e88d2b4958943bb5a88b4239d7e06fa5f98d43e3b58dd7469a2bef668b91b3754774ed71cf99a8f42c1aec569c14dda1f0f0ca9dc248cf37512e10591a6dc5b338302bab315115272e8b9c8d83030007520c8069fc2e31ae4023e660970329533a9af27c5b99c9bfff9772612aff48db978627d590ff8b540d2f4ab981b24a0beb3c6c898764183a1682fbfd4c5fce64c18bbf59fe35cb1cb797d40e8ec4d90bddb3e70831ab534179a5feac8ef2feba40cfcb1270bbaf5355e1cfca189ec62cb3da9d01a7810b7776eec544f534d83ffd2d0e602c0000000000000001509106e94ab3e29b51b6b283e5ba4e29c9e6783296c54bd141d70e32d6924f9a0f8709d7132801b382083ea5204e2509f7862c7e2b746b41d420945b71115f5356e8d05fa35625ba809b9333bdd933021c9eda631b24223dc9a5fb04b8e6eed176bfe827d411021f3751ba340fb85b261d350e2b8d2d40fbe9b3414c693227f9839e93402d0c1a064f08c6fdc14b913b8cc6426501b37a26f7d2d5a71641c2d685ace22d536bce58a5263c6af1d806046144d643c2b29327d36ebb64900f8fbcc3cb538ded16cfbdc85279020799b5a941ae9be52f103be89aa58207516585014ab2b48742c5762a6b2a51dd75eca3492891bb8e3fe9744a028271a9f5c585065a8247b431d1cf5b70e964ab902e24afb3449172f7eb03ee29dcbfd57cf97895212913dc3dcf06d7b99dd4e43078607744faa2f9bbe9844e6112841efbb011ef1ef59304be7183b8978ce37ebbf46f8869293d70a7ab3335be2e45117f1b15cc4b122598f272eeb5c2d29870770711d6f943c914bd4988c88f5fbbe727ace4e3480d4abd23a8937f20fc5340888e2ea30a452bcf03bafa47fcb42e8f47a35208df6181eb553a5e01cf7610277ee707f1f36d59254b2a9e3cae1f73548ee2f50e1fc6d255137636b9366144e8e99e4028c174ed059afe81b695f28bc5d174f69185f1d58a3f679d206600acf8396a2cafec639a994ed3ebbfefd580cf5ef0efaa722d97fd79d3b331cefd5244d96b1fe16840efca4896241f90cf7cb1e3c0db0e74c809b5facf8ea8e61b70af2023e862f6e923309c0cf8edbf2733d172c422175c5c2003f70c71daaef01baf01a584a28646faa8d8404593c57f45adb59ad83e25cfa5c75921b9535fb885b20f45b0a6777f589c9ad4c91a292b05ce34165a90221fbf9df66959685466b975062713b777221a3bf131ddd35fd2f3801f73198e12f06b01001881c34c642740025db6f5734d67e0dd6d2045c76a9079ead60426b2620d6e688015b0683905f03c0c4d12a6a82f98122538955575c8fb8e53ea9f34283614aa8d379da59573e9a9e48301269495408654efcbe150c52e76132e6e0b44301c45ce8a0dee943ba28009ecc1b0dbefbc08452401d4c0babcb2ddc3dedb735533f3a06cf7949ab1cb7198a0ff5a6240f38d2441dbadeba6ed80da9be1d510d465c93a1018ecfc155c60126a7b04d5e79d667ea8bff051eca6e21f49aa42c0dbc6a5c3fec62041cf8dfdaa3d1dad095bb053af091befd3eebd5f75f45c8d3adab253adcec1263b336204b11a8fabf61efd9747aa5b49055fa7589ac3c2eb91df786e263a9a14332b5c9a83192d2c6f3924971f4a4c4464c599dcf12bc5b227a0ffed09e05783a55848257b2c382589e91ba09d36dd3b6e1711357c05346fe6cbb2249b5ed055fec7370e6f275027f59c45b6c6066efcb9df1aeb50487361b0348bef55c6ccb6f9fb01edb1485cfedbc97639bee05427da7cb616482845ac9fc9760d98b5b8ec202546d01d017ab6af0bc6a21870631dab35bde3ab43085e9bd24d88e20e6cfd13827419a7fb8f9b5e1f86db33a426e81697c00441e37a253084416be6c3fa91e714b43b0b3b4e05041337392715624f605aaf51eadb2da762510092be6d43a49ec6c2f6c9aa2d0de21c29c729c1d4c743f5ed2cc429a3339c4d3fb83bf1f38b535d54e6721a69499868e414861acb664932f01672ec9ff974dda1360e70f9024180f8d31137eabb27ded4acd06338cde5ca1df8b3bb638050d3b914cffb25356edc0079440386f62e9c61c5ff268b09d17e5b66d7be1baf0d10b73ba0a48e9add8e7079962fd00f2bf9c301d7f325a31179d41fd5eed2761103526367d0737a9c30cbeeb75bfa402d0b4d0bce1dacef74e886a259db898b6aa75b254858df1a7650ef025dfd5148d2b7114492325ee1aadfdd57aefc3d54332e3c098f1d9b23d1f7acfa014a4acaf3b3a2db08e1bce03e7fe07d8b28f6346d0411b19c10c59204ef1634c90be42d27ac590c4e53df7b207d5383b8abbe7944bdfb490c1045e30dc44ec85314f640f991a9622220b59feacf921241711d65d3c7e5cd64155c8a2098bc2616d68ae925d7822783f86c717fcb44aeacacc932648e498eeffb2c3918cc2422c72c7c4ed15bf699b53e232fc9bad03379a7c2f518c94e7f67a46830f40cc9627e152f72619c719399dbd7015111d888987d7af55ec8a3d3f407f1ac43f9e7396bb10756013bdefde3b53a870d734c54af10954a6ac4d2ae23a4e5d617417709638bda6bd847a3cef272a2141ca0d466ba3882702b3dd85a18b6f9989d82d1081220370fb6294aa07b9a2b6fa413b98acc49d9f2ceb95f42b6a322637581dd1e788f11c6b5cb62fcb38090a3ed4970e80c60a1c3becad043f637c67175869f7a87c8bf87171800bbd48e0e117923ea5b86346f6480e53949d49d01f567373aa5b560fb9ed3b1415a0a994381913e282ba6b13a085c741fa5258845845214484cf8ba6b614f687c34c680f6b2f1e5b6120b78ceff31aa8754408987a44d56f2cc8f59583fc31692bc9994eeb46aa5d80cc97acc4f889bbaec699e86d960df6af1550edda3e2d6f9a436bc8b7310975e8886bd7c87a5527515d42a87d1067aa6381787f668c0e71ceec36a7d529feb7e395ca9ac112335dd6889cb9922215021a75430299de89abd01649aaeb710531641b8b5e22e06b83ec06ef047955753dc23e7f20a696c3304bcc4ef2448dbef14267254d04adfd4ffaa71987903980376b53d17295f5d48c4b5eef378710df218471ded2ad1cfef892c2179b18cb55b06ff6fdb706c1674d68a5355016404582f6330a41b40f77aba2e91031b2c472055ebd6e8e5f6541fc7ca8a014e47e62c168cefca69d56c278362fe7ca637bb4eefa438457bd236fc9d737bdede31832df475a8656795ba37d1d19053fbd8eeec98f9099e8b0317fd940f499fb424a4e80a976f5bb7a3f343f64ff1faa641ccce6e1a911a3beeadc79365657aee1f343a9889b5cf43c797ee8dc1353b28a70de697d89b3c233dc4c31a4ae25e40664029ddbf6aecba7c0908905d526bb4f884b54d2dab5b56b7eae5c5c10d180fc39428796358629b4576ca0f1a5bc30567167bfe6bdbd8cf999fa9b03af696f2ca106ff1aff483d2f39734ffa93a313db61b19035a7d6d2862817f148986a0dfbfa0df5303d264f5eb66f9ae18eafe8d5f1b5898895f73d57b6b9e15c787ff600fe9ea51d0d0318159aeb72e9d613c06b0ae54db18593a4246522a00b7453092f0c7dc279c8ef174801902c0d110b053aab4d59e2aa021560c9e2df6b6b78ee57e9cf7ac86143ec27218261096f91d4061403b9915bcb242d5e286a2ff29e0bb85bf275d44b8c57f8db4937401798bced361e23109c9076702b230d9a644817d348c1a24c816d03006292830c3f606b6066e056df3079f8bbb3d67dce48668bb56e2cf14be4afed8c48f9b13bdaa9ad5241a53179ff480866cb4c8ea90dd4b9f386fe700ea0d7122ce62738febe7f331a9aa0bdcbd64c2fdb6f5feee4ccfa4a594c318df2880de70d10096b40c33b5b0c067bcc1515924f79b4857fdf1781fcb736e706ef76e000f9537140a53fec81ba0a1383e06b8e1fa9532c86d9f54d551bd97c5c888cb1809fcdcb96a4f715c8b7d2dcca1bd0d841bd8920765e4c7dbf2b5b2cd909918d7dff6e17b2dd7d49bacc824a392f9e37ef7f2236b89cfa251156433b4a8fe3fc2f95b967496042f1919e3ab56313c103cba59bf828b7ee1538f37d361c04fdd50dff39e94443b3318e5176b40f7a86ea8c03ae5e0d9e9ca7d88b6d243d37f616e01c88d6337ed3ddc3ec37529953ea143b22612938a7d1504403265fd3fc08c456d6ca5d4f57117e623fc6583c9767a8564a031d240aee2b60a842dbe0697d72b48cb28986cbce7711fde84165a7ad3d5ea8ace5cceefc95782a76cc5e53704a4b5aa744e77e870bda92bd0cfc819b3c3e1d10f601e8ab9623544fb657647b8be5a19352cbbb3bbaa895e010299f93f4b02a3a13ec1b8f200b5da3a70cbfd7834943e2c1e56780ea46818ad1ea7c68c1eb60dc4202d8f84c40253dde4d3fd8f0fb5aa285b15fe011ecc7f6989953ae4e133d52b3699715a9f932e584b0314caeb459475ac3d31cdc72b4461898b955673573e04ef130c82774735b5f49973b97a6237f3f3c67935325786d9ba0dd602e4e4aad9d364b25b0cf0844ce4ecdc0ed984d8e70868b9628a5d8a0ba7eb49c3426012221edb2300a433cc5e2ed362fedebe8476c1d3e2e79e985dd9ed566bbd2944283b60959d94168c1b4b9e73f2a5aee8e6c4fb66abeda5a8ddd97eb4fe0bb1af00224ba09945be4c45d4401d2faf444efd98a3a46272d784188e4c4f2c2b207d1da756531ee1ca14ada5c9e021cb2f597ea9320949c728fe46b707e1e2b7994563b81d70603bc5a61221d6562f429c63f710147cf09178943e700028b5c3be0f0158a2190cc91ca3f1fa0e8b41301b011475f8e5ca96a469f4c39caefa0130fd567bf69f8d310e01ccf200253a3e654a3d310d2bf14eb5d62407b306c6719685c696d83614b7e4c0e191273aaafb939d028f99a548f7216b996b5d1c4e1ed2c27fba1e1a7c754f8df8bd0ef3108f1050e740a7e34dd27137232513babeb0225901a1ecefcc03357f772864da16aec9f328f69c617e281f2067ab7942767728e065e882a0f30f73ef51c86d068001572c73c15dce07e8f0d2c7d237117b1f663928a1b2e6c49db3537bf518c442eaf52961b17b1c37c338ae8a813fbf69f8c23cff4c90a5488284c019f740491c111e8ce4785f6b718302671585e4362d064077b65d4cdbfc376616fa468ab34515a3b8b90037c3926f11e0957dfc9a0c3516e408e44baad1efc83049333b32d95cf09c0d7278e4be741afa104cd84315032fc0d48c1c47cd2c52e3f6dd0449ffc0709d45bed863c4380e6a298592d5b2ffc2aab8e6e9efc63f27f6ce3c50305c9f7734c70362a75b58ec8205643c51e03a5a40351858183ae11dfc2519eb4054ef40b356355700bc3cf5149df4bcc84efae657e10dfe9b9ed8cec210e88d8639639c44c3e12d000070000000");
	}
}
