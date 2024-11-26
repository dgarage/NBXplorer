using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using NBitcoin;
using NBitcoin.RPC;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin.Altcoins.Elements;
using Xunit;
using Xunit.Abstractions;
using System.Net.Http;
using System.IO;
using Dapper;
using NBXplorer.Configuration;
using NBXplorer.Backend;

using NBitcoin.Tests;
using System.Globalization;
using System.Net;
using NBXplorer.HostedServices;
using NBitcoin.Altcoins;

namespace NBXplorer.Tests
{
	public partial class UnitTest1
	{
		public UnitTest1(ITestOutputHelper helper)
		{
			Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
			Logs.LogProvider = new XUnitLoggerProvider(helper);
		}
		NBXplorerNetworkProvider _Provider = new NBXplorerNetworkProvider(ChainName.Regtest);
		private NBXplorerNetwork GetNetwork(INetworkSet network)
		{
			return _Provider.GetFromCryptoCode(network.CryptoCode);
		}

		[Fact]
		public void CanCreateNetworkProvider()
		{
			foreach (var networkType in new[] { ChainName.Mainnet, ChainName.Testnet, ChainName.Regtest })
			{
				_ = new NBXplorerNetworkProvider((ChainName)networkType);
			}
		}

		[Fact]
		public void CanFixedSizeCache()
		{
			FixedSizeCache<uint256, uint256> cache = new FixedSizeCache<uint256, uint256>(2, k => k);
			Assert.Equal(2, cache.MaxElementsCount);
			Assert.Throws<ArgumentNullException>(() => cache.Add(null));
			Assert.Throws<ArgumentNullException>(() => cache.Contains(null));
			uint256 previous = RandomUtils.GetUInt256();

			int evicted = 0;
			for (int i = 0; i < 10000; i++)
			{
				uint256 newItem = RandomUtils.GetUInt256();
				cache.Add(newItem);
				if (cache.Contains(previous))
					evicted++;
				Assert.True(cache.Contains(newItem));
				previous = newItem;
			}

			// Should be around 5000
			Assert.True(evicted > 4000);
			Assert.True(evicted < 6000);
		}

		[FactWithTimeout]
		public async Task RepositoryCanTrackAddresses()
		{
			using (var tester = RepositoryTester.Create(true))
			{
				var dummy = new DirectDerivationStrategy(new ExtKey().Neuter().GetWif(Network.RegTest), false);
				await RepositoryCanTrackAddressesCore(tester, dummy);
			}
		}

		[FactWithTimeout]
		public async Task CanGetEvents()
		{
			using (var tester = RepositoryTester.Create(false))
			{
				var evt1 = new NewBlockEvent() { Height = 1 };
				var evt2 = new NewBlockEvent() { Height = 2 };
				var id = await tester.Repository.SaveEvent(evt1);
				Assert.Equal(1, id);

				// Well saved?
				var evts = await tester.Repository.GetEvents(-1);
				Assert.Single(evts);
				Assert.Equal(1, ((NewBlockEvent)evts[0]).Height);

				// But evt2 should be saved if there is a different eventId
				id = await tester.Repository.SaveEvent(evt2);
				Assert.Equal(2, id);

				// Let's see if both evts are returned correctly
				evts = await tester.Repository.GetEvents(-1);
				Assert.True(evts.Count == 2);
				Assert.Equal(1, ((NewBlockEvent)evts[0]).Height);
				Assert.Equal(2, ((NewBlockEvent)evts[1]).Height);

				// Or only 1 if we pass the first param
				evts = await tester.Repository.GetEvents(1);
				Assert.Single(evts);
				Assert.Equal(2, ((NewBlockEvent)evts[0]).Height);

				// Or only 1 if we pass limit
				evts = await tester.Repository.GetEvents(-1, 1);
				Assert.Single(evts);
				Assert.Equal(1, ((NewBlockEvent)evts[0]).Height);

				var evt3 = new NewBlockEvent() { Height = 3 };
				await tester.Repository.SaveEvent(evt3);

				evts = await tester.Repository.GetEvents(1, 1);
				Assert.Equal(2, evts[0].EventId);
				Assert.Single(evts);
				Assert.Equal(2, ((NewBlockEvent)evts[0]).Height);

				for (int i = 0; i < 20; i++)
				{
					var evt = new NewBlockEvent() { Height = 4 + i };
					await tester.Repository.SaveEvent(evt);
				}
				evts = await tester.Repository.GetEvents(0);
				Assert.Equal(23, evts.Count);

				// Test GetLatestEvents
				var latestEvtsNoArg = await tester.Repository.GetLatestEvents();
				Assert.Equal(10, latestEvtsNoArg.Count);
				Assert.Equal(14, ((NewBlockEvent)latestEvtsNoArg[0]).Height);
				Assert.Equal(23, ((NewBlockEvent)latestEvtsNoArg[9]).Height);

				var latestEvts1 = await tester.Repository.GetLatestEvents(1);
				Assert.Single(latestEvts1);
				Assert.Equal(23, ((NewBlockEvent)latestEvts1[0]).Height);

				var latestEvts10 = await tester.Repository.GetLatestEvents(10);
				Assert.Equal(10, latestEvts10.Count);
				Assert.Equal(14, ((NewBlockEvent)latestEvts10[0]).Height);
				Assert.Equal(23, ((NewBlockEvent)latestEvts10[9]).Height);

				var latestEvts50 = await tester.Repository.GetLatestEvents(50);
				Assert.Equal(23, latestEvts50.Count);
				Assert.Equal(1, ((NewBlockEvent)latestEvts50[0]).Height);
				Assert.Equal(23, ((NewBlockEvent)latestEvts50[22]).Height);

				int prev = 0;
				foreach (var item in evts)
				{
					Assert.Equal(prev + 1, ((NewBlockEvent)item).Height);
					Assert.Equal(prev + 1, item.EventId);
					prev = ((NewBlockEvent)item).Height;
				}
			}
		}


		[Fact]
		public void CanSerializeKeyPathFast()
		{
			using (var tester = RepositoryTester.Create(true))
			{
				var dummy = new DirectDerivationStrategy(new ExtKey().Neuter().GetWif(Network.RegTest), false);
				var seria = new Serializer(tester.Repository.Network);
				var keyInfo = new KeyPathInformation()
				{
					TrackedSource = new DerivationSchemeTrackedSource(dummy),
					DerivationStrategy = dummy,
					Feature = DerivationFeature.Change,
					KeyPath = new KeyPath("0/1"),
					Redeem = Script.Empty,
					ScriptPubKey = Script.Empty
				};
				var str = seria.ToString(keyInfo);
				for (int i = 0; i < 1500; i++)
				{
					seria.ToObject<KeyPathInformation>(str);
				}
			}
		}

		private static async Task RepositoryCanTrackAddressesCore(RepositoryTester tester, DerivationStrategyBase dummy)
		{
			Assert.Equal(2, await tester.Repository.GenerateAddresses(dummy, DerivationFeature.Deposit, 2));
			var keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(0).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("0/0"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy.ToString(), dummy.ToString());

			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(1).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("0/1"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy.ToString(), dummy.ToString());

			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(2).ScriptPubKey);
			Assert.Null(keyInfo);
			Assert.Equal(28, await tester.Repository.GenerateAddresses(dummy, DerivationFeature.Deposit));
			Assert.Equal(30, await tester.Repository.GenerateAddresses(dummy, DerivationFeature.Change));

			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(29).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("0/29"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy.ToString(), dummy.ToString());


			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Change).Derive(29).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/29"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy.ToString(), dummy.ToString());

			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(30).ScriptPubKey);
			Assert.Null(keyInfo);
			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Change).Derive(30).ScriptPubKey);
			Assert.Null(keyInfo);

			MarkAsUsed(tester.Repository, dummy, new KeyPath("1/5"));
			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Change).Derive(25).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/25"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy, dummy);

			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Change).Derive(36).ScriptPubKey);
			Assert.Null(keyInfo);

			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(30).ScriptPubKey);
			Assert.Null(keyInfo);

			for (int i = 0; i < 10; i++)
			{
				Assert.Equal(0, tester.Repository.GenerateAddresses(dummy, DerivationFeature.Deposit).Result);
				MarkAsUsed(tester.Repository, dummy, new KeyPath("0/" + i));
			}
			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(30).ScriptPubKey);
			Assert.Null(keyInfo);
			MarkAsUsed(tester.Repository, dummy, new KeyPath("0/10"));
			Assert.Equal(11, tester.Repository.GenerateAddresses(dummy, DerivationFeature.Deposit).Result);
			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(30).ScriptPubKey);
			Assert.NotNull(keyInfo);

			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(39).ScriptPubKey);
			Assert.NotNull(keyInfo);

			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(41).ScriptPubKey);
			Assert.Null(keyInfo);

			//No op
			MarkAsUsed(tester.Repository, dummy, new KeyPath("1/6"));
			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Change).Derive(29).ScriptPubKey);
			Assert.NotNull(keyInfo);
			Assert.Equal(new KeyPath("1/29"), keyInfo.KeyPath);
			Assert.Equal(keyInfo.DerivationStrategy.ToString(), dummy.ToString());
			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Change).Derive(30).ScriptPubKey);
			Assert.Null(keyInfo);
		}

		private static void MarkAsUsed(Repository repository, DerivationStrategyBase strat, KeyPath keyPath)
		{
			var script = strat.GetDerivation(keyPath).ScriptPubKey.ToHex();
			using var conn = repository.ConnectionFactory.CreateConnection().GetAwaiter().GetResult();
			conn.Execute("UPDATE descriptors_scripts SET used='t' WHERE code=@code AND script=@script", new { code = repository.Network.CryptoCode, script });
		}

		[FactWithTimeout]
		public async Task CanEasilySpendUTXOs()
		{
			using (var tester = ServerTester.Create())
			{
				var userExtKey = new ExtKey();
				var userDerivationScheme = tester.Client.Network.DerivationStrategyFactory.CreateDirectDerivationStrategy(userExtKey.Neuter(), new DerivationStrategyOptions()
				{
					ScriptPubKeyType = ScriptPubKeyType.Legacy
				});
				await tester.Client.TrackAsync(userDerivationScheme);

				// Send 1 BTC
				var newAddress = tester.Client.GetUnused(userDerivationScheme, DerivationFeature.Direct);
				var txId = tester.SendToAddress(newAddress.ScriptPubKey, Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(userDerivationScheme, txId);
				var utxos = tester.Client.GetUTXOs(userDerivationScheme);
				tester.RPC.Generate(1);
				tester.Notifications.WaitForBlocks();

				// Send 1 more BTC
				newAddress = tester.Client.GetUnused(userDerivationScheme, DerivationFeature.Deposit);
				txId = tester.SendToAddress(newAddress.ScriptPubKey, Money.Coins(1.1m));
				tester.Notifications.WaitForTransaction(userDerivationScheme, txId);
				utxos = tester.Client.GetUTXOs(userDerivationScheme);

				var balance = tester.Client.GetBalance(userDerivationScheme);
				Assert.Equal(Money.Coins(1.0m), balance.Confirmed);
				Assert.Equal(Money.Coins(1.1m), balance.Unconfirmed);
				Assert.Equal(Money.Coins(2.1m), balance.Total);

				utxos = tester.Client.GetUTXOs(userDerivationScheme);
				Assert.Equal(2, utxos.GetUnspentCoins().Length);
				for (int i = 0; i < 3; i++)
				{
					var changeAddress = tester.Client.GetUnused(userDerivationScheme, DerivationFeature.Change);
					var coins = utxos.GetUnspentCoins();
					var keys = utxos.GetKeys(userExtKey);
					TransactionBuilder builder = tester.Network.CreateTransactionBuilder();
					builder.AddCoins(coins);
					builder.AddKeys(keys);
					builder.Send(new Key(), Money.Coins(0.5m));
					builder.SetChange(changeAddress.ScriptPubKey);

					var fallbackFeeRate = new FeeRate(Money.Satoshis(100), 1);
					var feeRate = tester.Client.GetFeeRate(1, fallbackFeeRate).FeeRate;

					builder.SendEstimatedFees(feeRate);
					var tx = builder.BuildTransaction(true);
					Assert.True(builder.Verify(tx));
					Assert.True(tester.Client.Broadcast(tx).Success);
					tester.Notifications.WaitForTransaction(userDerivationScheme, tx.GetHash());
					utxos = tester.Client.GetUTXOs(userDerivationScheme);

					if (i == 0)
						Assert.Equal(2, utxos.GetUnspentCoins().Length);

					Assert.Contains(utxos.GetUnspentCoins(), u => u.ScriptPubKey == changeAddress.ScriptPubKey);
					Assert.Contains(utxos.Unconfirmed.UTXOs, u => u.ScriptPubKey == changeAddress.ScriptPubKey && u.Feature == DerivationFeature.Change);
				}
			}
		}

		[FactWithTimeout]
		public async Task CanCreatePSBT()
		{
			using (var tester = ServerTester.Create())
			{
				// We need to check if we can get utxo information of segwit utxos
				var segwit = await tester.RPC.GetNewAddressAsync(new GetNewAddressRequest()
				{
					AddressType = AddressType.Bech32
				});
				var txId = await tester.RPC.SendToAddressAsync(segwit, Money.Coins(0.01m));
				var newTx = await tester.RPC.GetRawTransactionAsync(txId);
				var coin = newTx.Outputs.AsCoins().First(c => c.ScriptPubKey == segwit.ScriptPubKey);
				var spending = tester.Network.CreateTransactionBuilder()
					.AddCoins(coin)
					.SendAll(new Key().PubKey.ScriptPubKey)
					.SubtractFees()
					.SendFees(Money.Satoshis(1000))
					.BuildTransaction(false);
				var spendingPSBT = (await tester.Client.UpdatePSBTAsync(new UpdatePSBTRequest()
				{
					PSBT = PSBT.FromTransaction(spending, tester.Network)
				})).PSBT;
				Assert.NotNull(spendingPSBT.Inputs[0].WitnessUtxo);
				///////////////////////////

				//CanCreatePSBTCore(tester, ScriptPubKeyType.SegwitP2SH);
				//CanCreatePSBTCore(tester, ScriptPubKeyType.Segwit);
				//CanCreatePSBTCore(tester, ScriptPubKeyType.Legacy);
				CanCreatePSBTCore(tester, ScriptPubKeyType.TaprootBIP86);

				// If we build a list of unconf transaction which is too long, the CreatePSBT should
				// fail rather than create a transaction that can't be broadcasted.
				var userExtKey = new ExtKey();
				var userDerivationScheme = tester.Client.Network.DerivationStrategyFactory.CreateDirectDerivationStrategy(userExtKey.Neuter(), new DerivationStrategyOptions()
				{
					ScriptPubKeyType = ScriptPubKeyType.Segwit
				});
				await tester.Client.TrackAsync(userDerivationScheme);
				var newAddress = await tester.Client.GetUnusedAsync(userDerivationScheme, DerivationFeature.Direct);
				txId = await tester.SendToAddressAsync(newAddress.ScriptPubKey, Money.Coins(1.0m));
				await tester.RPC.GenerateAsync(1);
				Thread.Sleep(500);
				await tester.RPC.GenerateAsync(1);
				await tester.RPC.GenerateAsync(1);
				for (int i = 0; i < 26; i++)
				{
					try
					{
						var psbt = await tester.Client.CreatePSBTAsync(userDerivationScheme, new CreatePSBTRequest()
						{
							Destinations = {
							new CreatePSBTDestination()
							{
								Destination = newAddress.Address,
								SweepAll = true
							}
						},
							FeePreference = new FeePreference() { ExplicitFeeRate = new FeeRate(2.0m) }
						});
						if (i == 25)
							Assert.Fail("CreatePSBT shouldn't have created a PSBT with a UTXO having too many ancestors");
						psbt.PSBT.SignAll(userDerivationScheme, userExtKey);
						psbt.PSBT.Finalize();
						Assert.True((await tester.Client.BroadcastAsync(psbt.PSBT.ExtractTransaction())).Success);
					}
					catch (NBXplorerException ex)
					{
						if (i == 25)
							Assert.Equal("not-enough-funds", ex.Error.Code);
						else
							throw;
					}
				}
			}
		}

		private static void CanCreatePSBTCore(ServerTester tester, ScriptPubKeyType type)
		{
			var userExtKey = new ExtKey();
			var userDerivationScheme = tester.Client.Network.DerivationStrategyFactory.CreateDirectDerivationStrategy(userExtKey.Neuter(), new DerivationStrategyOptions()
			{
				ScriptPubKeyType = type
			});
			tester.Client.Track(userDerivationScheme);
			var userExtKey2 = new ExtKey();
			var userDerivationScheme2 = tester.Client.Network.DerivationStrategyFactory.CreateDirectDerivationStrategy(userExtKey2.Neuter(), new DerivationStrategyOptions()
			{
				ScriptPubKeyType = type
			});
			tester.Client.Track(userDerivationScheme2);
			var newAddress2 = tester.Client.GetUnused(userDerivationScheme2, DerivationFeature.Deposit, skip: 2);

			// Send 1 BTC
			var newAddress = tester.Client.GetUnused(userDerivationScheme, DerivationFeature.Direct);
			var txId = tester.SendToAddress(newAddress.ScriptPubKey, Money.Coins(1.0m));
			tester.Notifications.WaitForTransaction(userDerivationScheme, txId);
			var utxos = tester.Client.GetUTXOs(userDerivationScheme);

			// Send 1 more BTC
			newAddress = tester.Client.GetUnused(userDerivationScheme, DerivationFeature.Deposit);
			txId = tester.SendToAddress(newAddress.ScriptPubKey, Money.Coins(1.0m));
			tester.Notifications.WaitForTransaction(userDerivationScheme, txId);
			utxos = tester.Client.GetUTXOs(userDerivationScheme);

			Logs.Tester.LogInformation("Let's check that if we can select all coins");
			{
				var req = new CreatePSBTRequest()
				{
					Destinations =
					{
						new CreatePSBTDestination()
						{
							Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
							Amount = Money.Coins(0.5m)
						}
					},
					FeePreference = new FeePreference()
					{
						ExplicitFee = Money.Coins(0.00001m),
					}
				};
				var minimumInputs = tester.Client.CreatePSBT(userDerivationScheme, req);
				req.SpendAllMatchingOutpoints = true;
				var spendAllOutpoints = tester.Client.CreatePSBT(userDerivationScheme, req);
				var input = Assert.Single(minimumInputs.PSBT.Inputs);
				if (type == ScriptPubKeyType.TaprootBIP86)
				{
					Assert.Equal(TaprootSigHash.Default, input.TaprootSighashType);
					Assert.Empty(input.HDKeyPaths);
					Assert.Single(input.HDTaprootKeyPaths);
					Assert.NotNull(input.TaprootInternalKey);
				}
				Assert.Equal(2, spendAllOutpoints.PSBT.Inputs.Count);
			}

			utxos = tester.Client.GetUTXOs(userDerivationScheme);
			Assert.Equal(2, utxos.GetUnspentCoins().Length);
			for (int i = 0; i < 3; i++)
			{
				var substractFee = i == 1;
				var explicitFee = i == 2;
				var psbt = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
				{
					Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
								Amount = Money.Coins(0.5m),
								SubstractFees = substractFee
							}
						},
					FeePreference = new FeePreference()
					{
						FallbackFeeRate = explicitFee ? null : new FeeRate(Money.Satoshis(100), 1),
						ExplicitFee = explicitFee ? Money.Coins(0.00001m) : null,
					},
					DisableFingerprintRandomization = true
				});
				Assert.Empty(psbt.PSBT.GlobalXPubs);
				Assert.Null(psbt.Suggestions);
				Assert.NotEqual(LockTime.Zero, psbt.PSBT.GetGlobalTransaction().LockTime);
				psbt.PSBT.SignAll(userDerivationScheme, userExtKey);
				Assert.True(psbt.PSBT.TryGetFee(out var fee));
				if (explicitFee)
					Assert.Equal(Money.Coins(0.00001m), fee);
				Assert.Equal(-(Money.Coins(0.5m) + (substractFee ? Money.Zero : fee)), psbt.PSBT.GetBalance(userDerivationScheme, userExtKey));
				psbt.PSBT.Finalize();
				var tx = psbt.PSBT.ExtractTransaction();
				Assert.True(tester.Client.Broadcast(tx).Success);
				tester.Notifications.WaitForTransaction(userDerivationScheme, tx.GetHash());
				utxos = tester.Client.GetUTXOs(userDerivationScheme);
				if (i == 0)
					Assert.Equal(2, utxos.GetUnspentCoins().Length);
				Assert.Contains(utxos.GetUnspentCoins(), u => u.ScriptPubKey == psbt.ChangeAddress.ScriptPubKey);
				Assert.Contains(utxos.Unconfirmed.UTXOs, u => u.ScriptPubKey == psbt.ChangeAddress.ScriptPubKey && u.Feature == DerivationFeature.Change);
			}

			var balance = tester.Client.GetUTXOs(userDerivationScheme).GetUnspentCoins().Select(c => c.Amount).Sum();
			var psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
								SweepAll = true
							}
						},
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.00001m),
				}
			});
			Assert.Equal(-balance, psbt2.PSBT.GetBalance(userDerivationScheme, userExtKey));
			Assert.Null(psbt2.ChangeAddress);

			Logs.Tester.LogInformation("Let's check that if ReserveChangeAddress is false, all call to CreatePSBT send the same change address");
			psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
								Amount = Money.Coins(0.3m),
							}
						},
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ReserveChangeAddress = false
			});
			var changeAddress = psbt2.ChangeAddress;

			psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
								Amount = Money.Coins(0.3m),
							}
						},
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ReserveChangeAddress = false
			});
			Assert.Equal(changeAddress, psbt2.ChangeAddress);
			Logs.Tester.LogInformation("Let's check that if ReserveChangeAddress is true, next call to CreatePSBT will create a new change address");
			psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
								Amount = Money.Coins(0.3m),
							}
						},
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ReserveChangeAddress = true
			});
			Assert.Equal(changeAddress, psbt2.ChangeAddress);
			var dest = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network);
			psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				RBF = false,
				Seed = 0,
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = dest,
								Amount = Money.Coins(0.3m),
							}
						},
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ReserveChangeAddress = false
			});
			Assert.NotEqual(changeAddress, psbt2.ChangeAddress);
			changeAddress = psbt2.ChangeAddress;

			Logs.Tester.LogInformation("Let's check that we can use the reserved change as explicit change and end up with the same psbt");
			var psbt3 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				RBF = false,
				Seed = 0,
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = dest,
								Amount = Money.Coins(0.3m),
							}
						},
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ExplicitChangeAddress = psbt2.ChangeAddress
			});
			Assert.Equal(psbt2.PSBT, psbt3.PSBT);

			Logs.Tester.LogInformation("Let's change that if ReserveChangeAddress is true, but the transaction fails to build, no address get reserverd");
			var ex = Assert.Throws<NBXplorerException>(() => psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
								Amount = Money.Coins(0.3m),
							}
						},
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(99999m),
				},
				ReserveChangeAddress = true
			}));
			Assert.False(psbt2.PSBT.GetOriginalTransaction().RBF);
			Assert.Equal("not-enough-funds", ex.Error.Code);
			psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				RBF = true,
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
								Amount = Money.Coins(0.3m),
							}
						},
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ReserveChangeAddress = false
			});
			Assert.True(psbt2.PSBT.GetOriginalTransaction().RBF);
			Assert.Equal(changeAddress, psbt2.ChangeAddress);
			foreach (var input in psbt2.PSBT.GetGlobalTransaction().Inputs)
			{
				Assert.Equal(new Sequence(Sequence.MAX_BIP125_RBF_SEQUENCE), input.Sequence);
			}

			Logs.Tester.LogInformation("Let's check that we can filter UTXO by confirmations");
			Logs.Tester.LogInformation("We have no confirmation, so we should not have enough money if asking for min 1 conf");
			ex = Assert.Throws<NBXplorerException>(() => psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
								Amount = Money.Coins(0.3m),
							}
						},
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ReserveChangeAddress = false,
				MinConfirmations = 1
			}));
			Assert.Equal("not-enough-funds", ex.Error.Code);

			Logs.Tester.LogInformation("But if we mine, this should become ok");
			tester.Explorer.Generate(1);
			tester.WaitSynchronized();
			psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
								Amount = Money.Coins(0.3m),
							}
						},
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ReserveChangeAddress = false,
				MinConfirmations = 1
			});
			// We always signed with lowR so this should be always true
			Assert.True(psbt2.Suggestions.ShouldEnforceLowR);

			Logs.Tester.LogInformation("Let's check includeOutpoint and excludeOutpoints");
			txId = tester.SendToAddress(newAddress.ScriptPubKey, Money.Coins(1.0m));
			tester.Notifications.WaitForTransaction(userDerivationScheme, txId);
			psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
								SweepAll = true
							}
						},
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ReserveChangeAddress = false
			});
			var outpoints = psbt2.PSBT.GetOriginalTransaction().Inputs.Select(i => i.PrevOut).ToArray();
			Assert.Equal(2, outpoints.Length);

			var request = new CreatePSBTRequest()
			{
				IncludeOnlyOutpoints = new List<OutPoint>() { outpoints[0] },
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
								SweepAll = true
							}
						},
				MinValue = Money.Coins(1.0m),
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ReserveChangeAddress = false
			};
			psbt2 = tester.Client.CreatePSBT(userDerivationScheme, request);

			var actualOutpoints = psbt2.PSBT.GetOriginalTransaction().Inputs.Select(i => i.PrevOut).ToArray();
			Assert.Single(actualOutpoints);
			Assert.Equal(outpoints[0], actualOutpoints[0]);
			request.MinValue = Money.Coins(0.1m);
			ex = Assert.Throws<NBXplorerException>(() => tester.Client.CreatePSBT(userDerivationScheme, request));
			Assert.Equal("not-enough-funds", ex.Error.Code);

			psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				ExcludeOutpoints = new List<OutPoint>() { outpoints[0] },
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
								SweepAll = true
							}
						},
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ReserveChangeAddress = false
			});

			actualOutpoints = psbt2.PSBT.GetOriginalTransaction().Inputs.Select(i => i.PrevOut).ToArray();
			Assert.Single(actualOutpoints);
			Assert.Equal(outpoints[1], actualOutpoints[0]);

			Logs.Tester.LogInformation("Let's check nLocktime and version");

			psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				Version = 2,
				RBF = false,
				LockTime = new LockTime(1_000_000),
				ExcludeOutpoints = new List<OutPoint>() { outpoints[0] },
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
								SweepAll = true
							}
						},
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ReserveChangeAddress = false
			});
			var txx = psbt2.PSBT.GetOriginalTransaction();
			Assert.Equal(new LockTime(1_000_000), txx.LockTime);
			Assert.Equal(2U, txx.Version);
			Assert.False(txx.RBF);
			foreach (var input in txx.Inputs)
			{
				Assert.Equal(Sequence.FeeSnipping, input.Sequence);
			}

			Logs.Tester.LogInformation("Spend to self should give us 2 outputs with hdkeys pre populated");

			psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = newAddress.Address,
								Amount = Money.Coins(0.0001m)
							},
							new CreatePSBTDestination()
							{
								Destination = newAddress2.Address,
								Amount = Money.Coins(0.0001m)
							}
						},
				DiscourageFeeSniping = false,
				RBF = false,
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ReserveChangeAddress = false
			});
			Assert.Equal(3, psbt2.PSBT.Outputs.Count);
			Assert.Equal(2, psbt2.PSBT.Outputs.Where(o => o.HDKeyPaths.Any()).Count());
			Assert.Single(psbt2.PSBT.Outputs.Where(o => o.HDKeyPaths.Any(h => h.Value.KeyPath == newAddress.KeyPath)));
			foreach (var input in psbt2.PSBT.GetGlobalTransaction().Inputs)
			{
				Assert.Equal(Sequence.Final, input.Sequence);
			}

			Logs.Tester.LogInformation("Let's check if we can update the PSBT if information is missing");
			var expected = psbt2.PSBT.Clone();
			var actual = psbt2.PSBT.Clone();
			foreach (var input in actual.Inputs)
			{
				input.HDKeyPaths.Clear();
				input.WitnessUtxo = null;
				input.WitnessScript = null;
				input.NonWitnessUtxo = null;
			}
			foreach (var output in actual.Outputs)
			{
				output.HDKeyPaths.Clear();
				output.WitnessScript = null;
			}
			Assert.NotEqual(expected, actual);
			actual = tester.Client.UpdatePSBT(new UpdatePSBTRequest() { PSBT = actual, DerivationScheme = userDerivationScheme }).PSBT;
			Assert.Equal(expected, actual);

			Assert.All(expected.Inputs, i => Assert.Equal(type != ScriptPubKeyType.Legacy, i.NonWitnessUtxo is null));

			Logs.Tester.LogInformation("We should be able to rebase hdkeys");

			var rootHD = new HDFingerprint(new byte[] { 0x04, 0x01, 0x02, 0x04 });
			psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = newAddress.Address,
								Amount = Money.Coins(0.0001m)
							},
							new CreatePSBTDestination()
							{
								Destination = newAddress2.Address,
								Amount = Money.Coins(0.0001m)
							}
						},
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ReserveChangeAddress = false,
				RebaseKeyPaths = new List<PSBTRebaseKeyRules>()
					{
						new PSBTRebaseKeyRules()
						{
							AccountKey = userDerivationScheme.GetExtPubKeys().First().GetWif(tester.Network),
							AccountKeyPath = new RootedKeyPath(rootHD, new KeyPath("49'/0'"))
						}
					},
				IncludeGlobalXPub = true
			});
			var globalXPub = psbt2.PSBT.GlobalXPubs[userDerivationScheme.GetExtPubKeys().First().GetWif(tester.Network)];
			Assert.Equal(new KeyPath("49'/0'"), globalXPub.KeyPath);

			Assert.Equal(3, psbt2.PSBT.Outputs.Count);
			Assert.Equal(2, psbt2.PSBT.Outputs.Where(o => o.HDKeyPaths.Any()).Count());
			var selfchange = Assert.Single(psbt2.PSBT.Outputs.Where(o => o.HDKeyPaths.Any(h => h.Key.GetAddress(type, tester.Network).ScriptPubKey == newAddress.ScriptPubKey)));
			Assert.All(psbt2.PSBT.Inputs.Concat<PSBTCoin>(new[] { selfchange }).SelectMany(i => i.HDKeyPaths), i =>
			{
				Assert.Equal(rootHD, i.Value.MasterFingerprint);
				Assert.StartsWith("49'/0'", i.Value.KeyPath.ToString());
				Assert.Equal(4, i.Value.KeyPath.Indexes.Length);
			});

			Logs.Tester.LogInformation("Let's check that if the explicit change is one of the destination, fee are calculated correctly");
			psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
			{
				Destinations =
						{
							new CreatePSBTDestination()
							{
								Destination = newAddress.Address,
								Amount = Money.Coins(0.0001m)
							}
						},
				ExplicitChangeAddress = newAddress.Address,
				FeePreference = new FeePreference()
				{
					FallbackFeeRate = new FeeRate(1.0m)
				},
				ReserveChangeAddress = true
			});
			Assert.True(psbt2.PSBT.TryGetEstimatedFeeRate(out var feeRate));
			Assert.Equal(new FeeRate(1.0m), feeRate);

			Logs.Tester.LogInformation("Let's check what happen when SubstractFees=true, but paying output doesn't have enough money");

			foreach (Money tooLowAmount in new[] { Money.Satoshis(1000), Money.Satoshis(600) })
			{
				ex = Assert.Throws<NBXplorerException>(() => tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
				{
					Destinations =
					{
					  new CreatePSBTDestination()
					  {
						Destination = newAddress.Address,
						Amount = tooLowAmount,
						SubstractFees = true
					  }
					},
					ExplicitChangeAddress = newAddress.Address,
					FeePreference = new FeePreference()
					{
						ExplicitFee = Money.Satoshis(1001)
					},
					ReserveChangeAddress = true
				}));
				Assert.Equal("output-too-small", ex.Error.Code);
			}

			Logs.Tester.LogInformation("Let's check what happens when the amout to send is too low");

			foreach (Money tooLowAmount in new[] { Money.Satoshis(100) })
			{
				ex = Assert.Throws<NBXplorerException>(() => tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
				{
					Destinations =
					{
						new CreatePSBTDestination()
						{
							Destination = newAddress.Address,
							Amount = tooLowAmount
						},
					},
					FeePreference = new FeePreference()
					{
						FallbackFeeRate = new FeeRate(1.0m)
					}
				}));
				Assert.Equal("output-too-small", ex.Error.Code);
				Assert.Equal(OutputTooSmallException.ErrorType.TooSmallBeforeSubtractedFee.ToString(), ex.Error.Reason);
			}

			if (type == ScriptPubKeyType.Segwit || type == ScriptPubKeyType.TaprootBIP86)
			{
				// some PSBT signers are incompliant with spec and require the non_witness_utxo even for segwit inputs

				Logs.Tester.LogInformation("Let's check that if we can create or update a psbt with non_witness_utxo filled even for segwit inputs");
				psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
				{
					Destinations =
					{
						new CreatePSBTDestination()
						{
							Destination = newAddress.Address,
							Amount = Money.Coins(0.0001m)
						}
					},
					FeePreference = new FeePreference()
					{
						FallbackFeeRate = new FeeRate(1.0m)
					},
					AlwaysIncludeNonWitnessUTXO = true
				});

				//in our case, we should have the tx to load this, but if someone restored the wallet and has a pruned node, this may not be set 
				foreach (var psbtInput in psbt2.PSBT.Inputs)
				{
					Assert.NotNull(psbtInput.NonWitnessUtxo);
				}
			}
		}

		[TheoryWithTimeout]
		[InlineData(true)]
		[InlineData(false)]
		public async Task CanDoubleSpend(bool onConfirmedUTXO)
		{
			using var tester = ServerTester.Create();
			var bobW = await tester.Client.GenerateWalletAsync(new GenerateWalletRequest() { ScriptPubKeyType = ScriptPubKeyType.Segwit });
			var bob = bobW.DerivationScheme;
			var bobAddr = await tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit, 0);


			var aId = tester.RPC.SendToAddress(bobAddr.ScriptPubKey, Money.Satoshis(100_000), new SendToAddressParameters() { Replaceable = true });
			var a = tester.Notifications.WaitForTransaction(bob, aId);
			var aIdx = a.TransactionData.Transaction.Outputs.FindIndex(o => o.ScriptPubKey == bobAddr.ScriptPubKey);
			var bobOutpoint = new OutPoint(aId, aIdx);
			if (onConfirmedUTXO)
			{
				tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));
			}
			var psbt = tester.Client.CreatePSBT(bob, new CreatePSBTRequest()
			{
				Destinations =
				{
					new CreatePSBTDestination()
					{
						Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
						Amount = Money.Satoshis(50_000)
					}
				},
				FeePreference = new FeePreference() { ExplicitFee = Money.Satoshis(500) },
				RBF = true
			}).PSBT;

			psbt = psbt.SignAll(ScriptPubKeyType.Segwit, bobW.AccountHDKey, bobW.AccountKeyPath);
			psbt.Finalize();
			await tester.Client.BroadcastAsync(psbt.ExtractTransaction());

			var utxos = tester.Client.GetUTXOs(bob);
			if (onConfirmedUTXO)
				Assert.Empty(utxos.SpentUnconfirmed);
			else
				Assert.Equal(bobOutpoint, Assert.Single(utxos.SpentUnconfirmed).Outpoint);

			var replacement = psbt = (await tester.Client.CreatePSBTAsync(bob, new CreatePSBTRequest()
			{
				Destinations =
				{
					new CreatePSBTDestination()
					{
						Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
						Amount = Money.Satoshis(40_000)
					}
				},
				IncludeOnlyOutpoints = new List<OutPoint>() { bobOutpoint },
				FeePreference = new FeePreference() { ExplicitFee = Money.Satoshis(2000) },
				RBF = true
			})).PSBT;

			Assert.Equal(replacement.Inputs[0].GetCoin().Outpoint, bobOutpoint);
			Assert.Equal(psbt.Inputs[0].GetCoin().Outpoint, bobOutpoint);
		}

		[Fact]
		public async Task ShowRBFedTransaction4()
		{
			// Fix #421: replacement tx not detected as replacement (replacing: [])
			// Create A.
			// Mine it
			// Spend A with B
			// Mine a block without B.
			// Double-Spend A with B'
			// Check that B' is replacing B in events
			using var tester = ServerTester.Create();
			var bobW = tester.Client.GenerateWallet();
			var bob = bobW.DerivationScheme;
			var bobAddr = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);
			var aId = tester.RPC.SendToAddress(bobAddr.ScriptPubKey, Money.Satoshis(100_000), new SendToAddressParameters() { Replaceable = true });
			var a = tester.Notifications.WaitForTransaction(bob, aId).TransactionData.Transaction;

			Logs.Tester.LogInformation("a: " + aId.ToString());
			tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));

			var anotherAddr = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network);
			var psbt = (await tester.Client.CreatePSBTAsync(bob, new CreatePSBTRequest()
			{
				Destinations =
				{
					new CreatePSBTDestination()
					{
						Destination = anotherAddr,
						Amount = Money.Satoshis(50_000)
					}
				},
				FeePreference = new FeePreference() { ExplicitFee = Money.Satoshis(500) },
				RBF = true
			})).PSBT;
			var preSignedPsbt = psbt.Clone();
			psbt.SignAll(ScriptPubKeyType.Segwit, bobW.AccountHDKey, bobW.AccountKeyPath);
			psbt.Finalize();
			var b = psbt.ExtractTransaction();
			Logs.Tester.LogInformation("b: " + b.GetHash().ToString());
			await tester.Client.BroadcastAsync(b);

			// Do not mine anything
			var block = uint256.Parse(JObject.Parse((await tester.RPC.SendCommandAsync("generateblock", anotherAddr.ToString(), new string[0])).ResultString)["hash"].Value<string>());
			tester.Notifications.WaitForBlocks(block);

			// b' shouldn't have any output belonging to our wallets.
			var bp = b.Clone();
			bp.Outputs[0].Value -= Money.Satoshis(5000); // Add some fee to bump the tx
			var psbt2 = PSBT.FromTransaction(bp, tester.Network);
			psbt2.UpdateFrom(preSignedPsbt);
			psbt2.SignAll(ScriptPubKeyType.Segwit, bobW.AccountHDKey, bobW.AccountKeyPath);
			psbt2.Finalize();
			bp = psbt2.ExtractTransaction();
			Logs.Tester.LogInformation("bp: " + bp.GetHash().ToString());
			Assert.True((await tester.Client.BroadcastAsync(bp)).Success);
			var bpEvent = tester.Notifications.WaitForTransaction(bob, bp.GetHash());
			Assert.Null(bpEvent.BlockId);
			Assert.Contains(bpEvent.Replacing, r => r == b.GetHash());

			tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));
			bpEvent = tester.Notifications.WaitForTransaction(bob, bp.GetHash());
			Assert.NotNull(bpEvent.BlockId);
			Assert.Contains(bpEvent.Replacing, r => r == b.GetHash());

			// Make sure there is no dups events on unconf txs
			await Task.Delay(100);
			var evts = await tester.Client.CreateLongPollingNotificationSession().GetEventsAsync();
			Assert.Single(evts.OfType<NewTransactionEvent>()
				.Where(t => t.BlockId is null && t.TransactionData.TransactionHash == bp.GetHash()));
		}

		[TheoryWithTimeout]
		[InlineData(true)]
		[InlineData(false)]
		public async Task ShowRBFedTransaction3(bool cancelB)
		{
			// Let's do a chain of two transactions implicating Bob A and B.
			// Then B get replaced by B'.
			// We should make sure that B' is still saved in the database, and B properly marked as replaced.
			// If cancelB is true, then B' output shouldn't be related to Bob.
			using var tester = ServerTester.Create();

			var bobW = await tester.Client.GenerateWalletAsync();
			var bob = bobW.DerivationScheme;
			
			var bobAddr = await tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit, 0);
			var bobAddr1 = await tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit, 1);

			var aId = tester.RPC.SendToAddress(bobAddr.ScriptPubKey, Money.Satoshis(100_000), new SendToAddressParameters() { Replaceable = true });
			var a = tester.Notifications.WaitForTransaction(bob, aId).TransactionData.Transaction;
			Logs.Tester.LogInformation("a: " + aId);

			// b shouldn't have any input belonging to our wallets.
			var changeAddr = a.Outputs.Where(o => o.ScriptPubKey != bobAddr.ScriptPubKey).First().ScriptPubKey;
			LockTestCoins(tester.RPC, new HashSet<Script>() { changeAddr });

			var bId = tester.RPC.SendToAddress(bobAddr1.ScriptPubKey, Money.Satoshis(200_000), new SendToAddressParameters() { Replaceable = true });
			var b = tester.Notifications.WaitForTransaction(bob, bId).TransactionData.Transaction;
			Logs.Tester.LogInformation("b: " + bId);

			// b' shouldn't have any output belonging to our wallets.
			var bp = b.Clone();
			var o = bp.Outputs.First(o => o.ScriptPubKey == bobAddr1.ScriptPubKey);
			if (cancelB)
				o.ScriptPubKey = changeAddr;
			o.Value -= Money.Satoshis(5000); // Add some fee to bump the tx
			foreach (var input in bp.Inputs)
			{
				input.ScriptSig = Script.Empty;
				input.WitScript = WitScript.Empty;
			}
			bp = await tester.RPC.SignRawTransactionAsync(bp);
			await tester.RPC.SendRawTransactionAsync(bp);
			Logs.Tester.LogInformation("bp: " + bp.GetHash());

			// If not a cancellation, B' should send an event, and replacing B
			if (!cancelB)
			{
				var evt = tester.Notifications.WaitForTransaction(bob, bp.GetHash());
				Assert.Equal(bId, Assert.Single(evt.Replacing));
			}

			tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));
			var bpr = await tester.Client.GetTransactionAsync(bp.GetHash());
			Assert.NotNull(bpr?.Transaction);
			Assert.Equal(1, bpr.Confirmations);
			var br = await tester.Client.GetTransactionAsync(b.GetHash());
			Assert.NotNull(br?.Transaction);
			Assert.Equal(bp.GetHash(), br.ReplacedBy);
			Assert.Equal(0, br.Confirmations);
		}

		[FactWithTimeout]
		public async Task ShowRBFedTransaction()
		{
			using (var tester = ServerTester.Create())
			{
				var bob = tester.CreateDerivationStrategy();
				var bobSource = new DerivationSchemeTrackedSource(bob);
				tester.Client.Track(bob);
				var a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);

				var payment1 = Money.Coins(0.04m);
				var payment2 = Money.Coins(0.08m);

				Logs.Tester.LogInformation("Tx1 get spent by Tx2, then Tx3 is replacing Tx1. So Tx1 and Tx2 should also appear replaced. Tx4 then spends Tx3.");
				var tx1 = tester.RPC.SendToAddress(a1.ScriptPubKey, payment1, new SendToAddressParameters() { Replaceable = true });
				tester.Notifications.WaitForTransaction(bob, tx1);
				Logs.Tester.LogInformation($"Tx1: {tx1}");
				var utxo = tester.Client.GetUTXOs(bob); //Wait tx received
				Assert.Equal(tx1, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);

				var a2 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);
				var tx2psbt = (await tester.Client.CreatePSBTAsync(bob, new CreatePSBTRequest()
				{
					RBF = false,
					Destinations = new List<CreatePSBTDestination>()
					{
						new CreatePSBTDestination()
						{
							SubstractFees = true,
							Destination = a2.Address,
							Amount = payment1,
						}
					},
					FeePreference = new FeePreference()
					{
						ExplicitFee = Money.Satoshis(400)
					}
				})).PSBT;
				tester.SignPSBT(tx2psbt);
				tx2psbt.Finalize();
				var tx2 = tx2psbt.ExtractTransaction();
				await tester.Client.BroadcastAsync(tx2);
				tester.Notifications.WaitForTransaction(bob, tx2.GetHash());
				Logs.Tester.LogInformation($"Tx2: {tx2.GetHash()}");

				var tx = tester.RPC.GetRawTransaction(tx1);
				var tx1t = tx.Clone();
				foreach (var input in tx.Inputs)
				{
					input.ScriptSig = Script.Empty; //Strip signatures
				}
				var change = tx.Outputs.First(o => o.Value != payment1);
				change.Value -= ((payment2 - payment1) + Money.Satoshis(5000)); //Add more fees
				var output = tx.Outputs.First(o => o.Value == payment1);
				output.Value = payment2;
				var replacement = tester.RPC.SignRawTransaction(tx);
				Logs.Tester.LogInformation($"Tx3: {replacement.GetHash()}");
				tester.RPC.SendRawTransaction(replacement);
				var txEvt = tester.Notifications.WaitForTransaction(bob, replacement.GetHash());

				// tx3 replace tx1, so tx2 should also be replaced
				Assert.Equal(2, txEvt.Replacing.Count);
				Assert.Contains(tx1, txEvt.Replacing);
				Assert.Contains(tx2.GetHash(), txEvt.Replacing);

				var prevUtxo = utxo;
				utxo = tester.Client.GetUTXOs(bob); //Wait tx received
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(replacement.GetHash(), utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);

				var txs = tester.Client.GetTransactions(bob);
				Assert.Single(txs.UnconfirmedTransactions.Transactions);
				Assert.Equal(replacement.GetHash(), txs.UnconfirmedTransactions.Transactions[0].TransactionId);
				Assert.Equal(tx1, txs.UnconfirmedTransactions.Transactions[0].Replacing);

				Assert.Equal(2, txs.ReplacedTransactions.Transactions.Count);
				Assert.Equal(tx2.GetHash(), txs.ReplacedTransactions.Transactions[0].TransactionId);
				Assert.False(txs.ReplacedTransactions.Transactions[0].Replaceable);
				Assert.Equal(tx1, txs.ReplacedTransactions.Transactions[1].TransactionId);
				Assert.False(txs.ReplacedTransactions.Transactions[1].Replaceable);

				Assert.Equal(replacement.GetHash(), txs.ReplacedTransactions.Transactions[0].ReplacedBy);

				Logs.Tester.LogInformation("Rebroadcasting the replaced TX should fail");
				var broadcaster = tester.GetService<Broadcaster>();
				var check = tester.GetService<CheckMempoolTransactionsPeriodicTask>();
				var rebroadcast = await broadcaster.Broadcast(tester.Client.Network, tx1t);
				Assert.True(rebroadcast.MempoolConflict);

				Logs.Tester.LogInformation("Rebroadcasting the replacement should succeed");
				rebroadcast = await broadcaster.Broadcast(tester.Client.Network, replacement);
				Assert.True(rebroadcast.AlreadyInMempool);

				Logs.Tester.LogInformation("Now tx4 is spending the tx3");
				var tx4psbt = (await tester.Client.CreatePSBTAsync(bob, new CreatePSBTRequest()
				{
					RBF = true,
					Destinations = new List<CreatePSBTDestination>()
					{

					},
					FeePreference = new FeePreference()
					{
						ExplicitFee = Money.Satoshis(400)
					}
				})).PSBT;
				tester.SignPSBT(tx4psbt);
				tx4psbt.Finalize();
				var tx4 = tx4psbt.ExtractTransaction();
				Logs.Tester.LogInformation($"Tx4: {tx4.GetHash()}");
				var r = await tester.Client.BroadcastAsync(tx4);
				tester.Notifications.WaitForTransaction(bob, tx4.GetHash());
				txs = tester.Client.GetTransactions(bob);
				Assert.Equal(2, txs.UnconfirmedTransactions.Transactions.Count);
				Assert.True(txs.UnconfirmedTransactions.Transactions[0].Replaceable);
				// Can't replace a transaction inside an unconfirmed chain.
				Assert.False(txs.UnconfirmedTransactions.Transactions[1].Replaceable);

				tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));

				Logs.Tester.LogInformation("Rebroadcasting the replaced TX should clean two tx record (tx3 and tx4) from the list");
				rebroadcast = await broadcaster.Broadcast(tester.Client.Network, replacement);
				Assert.True(rebroadcast.MissingInput);
				Assert.False(rebroadcast.MempoolConflict);

				// TXs has been double spent, but unless we rebroadcast it, NBX doesn't know, iterating transactions
				// will ask rebroadcaster to broadcast
				await check.Do(default);
				txs = await tester.Client.GetTransactionsAsync(bob);
				Assert.Empty(txs.UnconfirmedTransactions.Transactions);
			}
		}

		[FactWithTimeout]
		public async Task CanGetUnusedAddresses()
		{
			using (var tester = ServerTester.Create())
			{
				var bob = tester.CreateDerivationStrategy();
				var utxo = tester.Client.GetUTXOs(bob); //Track things do not wait

				var a1 = await tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit, 0);
				Assert.Null(a1);
				await tester.Client.TrackAsync(bob);
				a1 = await tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit, 0);
				Assert.NotNull(a1);
				Assert.NotNull(a1.Address);
				Assert.Equal(a1.ScriptPubKey, (await tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit, 0)).ScriptPubKey);
				Assert.Equal(a1.ScriptPubKey, bob.GetDerivation(new KeyPath("0/0")).ScriptPubKey);

				var a2 = await tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit, skip: 1);
				Assert.Equal(a2.ScriptPubKey, bob.GetDerivation(new KeyPath("0/1")).ScriptPubKey);

				var a3 = await tester.Client.GetUnusedAsync(bob, DerivationFeature.Change, skip: 0);
				Assert.Equal(a3.ScriptPubKey, bob.GetDerivation(new KeyPath("1/0")).ScriptPubKey);

				var a4 = await tester.Client.GetUnusedAsync(bob, DerivationFeature.Direct, skip: 1);
				Assert.Equal(a4.ScriptPubKey, bob.GetDerivation(new KeyPath("1")).ScriptPubKey);

				Assert.Null(tester.Client.GetUnused(bob, DerivationFeature.Change, skip: 30));

				a3 = await tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit, skip: 2);
				Assert.Equal(new KeyPath("0/2"), a3.KeyPath);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
				//   0/0 and 0/2 used
				tester.SendToAddress(a1.ScriptPubKey, Money.Coins(1.0m));
				tester.SendToAddress(a3.ScriptPubKey, Money.Coins(1.0m));
				var txId = tester.SendToAddress(a4.ScriptPubKey, Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(bob, txId);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
				a1 = await tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit, 0);
				Assert.Equal(a1.ScriptPubKey, bob.GetDerivation(new KeyPath("0/1")).ScriptPubKey);
				a2 = await tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit, skip: 1);
				Assert.Equal(new KeyPath("0/3"), a2.KeyPath);
				Assert.Equal(a2.ScriptPubKey, bob.GetDerivation(new KeyPath("0/3")).ScriptPubKey);

				a4 = await tester.Client.GetUnusedAsync(bob, DerivationFeature.Direct, skip: 1);
				Assert.Equal(a4.ScriptPubKey, bob.GetDerivation(new KeyPath("2")).ScriptPubKey);

			}
		}

		CancellationToken Cancel => new CancellationTokenSource(5000).Token;

		[Fact]
		[Trait("Azure", "Azure")]
		public async Task CanSendAzureServiceBusNewBlockEventMessage()
		{

			Assert.False(string.IsNullOrWhiteSpace(AzureServiceBusTestConfig.ConnectionString), "Please Set Azure Service Bus Connection string in TestConfig.cs AzureServiceBusTestConfig Class. ");

			Assert.False(string.IsNullOrWhiteSpace(AzureServiceBusTestConfig.NewBlockQueue), "Please Set Azure Service Bus NewBlockQueue name in TestConfig.cs AzureServiceBusTestConfig Class. ");

			Assert.False(string.IsNullOrWhiteSpace(AzureServiceBusTestConfig.NewBlockTopic), "Please Set Azure Service Bus NewBlockTopic name in TestConfig.cs AzureServiceBusTestConfig Class. ");

			Assert.False(string.IsNullOrWhiteSpace(AzureServiceBusTestConfig.NewBlockSubscription), "Please Set Azure Service Bus NewBlock Subscription name in TestConfig.cs AzureServiceBusTestConfig Class. ");


			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), true);
				tester.Client.Track(pubkey);

				IQueueClient blockClient = new QueueClient(AzureServiceBusTestConfig.ConnectionString, AzureServiceBusTestConfig.NewBlockQueue);
				ISubscriptionClient subscriptionClient = new SubscriptionClient(AzureServiceBusTestConfig.ConnectionString, AzureServiceBusTestConfig.NewBlockTopic, AzureServiceBusTestConfig.NewBlockSubscription);

				//Configure Service Bus Subscription callback

				//We may have existing messages from other tests - push all message to a LIFO stack
				var busMessages = new ConcurrentStack<Microsoft.Azure.ServiceBus.Message>();

				var messageHandlerOptions = new MessageHandlerOptions((e) =>
				{
					throw e.Exception;
				})
				{
					// Maximum number of Concurrent calls to the callback `ProcessMessagesAsync`, set to 1 for simplicity.
					// Set it according to how many messages the application wants to process in parallel.
					MaxConcurrentCalls = 1,

					// Indicates whether MessagePump should automatically complete the messages after returning from User Callback.
					// False below indicates the Complete will be handled by the User Callback as in `ProcessMessagesAsync` below.
					AutoComplete = false
				};

				//Service Bus Topic Message Handler
				subscriptionClient.RegisterMessageHandler(async (m, t) =>
				{
					busMessages.Push(m);
					await subscriptionClient.CompleteAsync(m.SystemProperties.LockToken);
				}, messageHandlerOptions);


				//Test Service Bus Queue
				//Retry 10 times 
				var retryPolicy = new RetryExponential(new TimeSpan(0, 0, 0, 0, 500), new TimeSpan(0, 0, 1), 10);

				var messageReceiver = new MessageReceiver(AzureServiceBusTestConfig.ConnectionString, AzureServiceBusTestConfig.NewBlockQueue, ReceiveMode.ReceiveAndDelete, retryPolicy);
				Microsoft.Azure.ServiceBus.Message msg = null;

				//Clear any existing messages from queue
				while (await messageReceiver.PeekAsync() != null)
				{
					// Batch the receive operation
					var brokeredMessages = await messageReceiver.ReceiveAsync(300);
				}
				await messageReceiver.CloseAsync();     //Close queue , otherwise receiver will consume our test message

				messageReceiver = new MessageReceiver(AzureServiceBusTestConfig.ConnectionString, AzureServiceBusTestConfig.NewBlockQueue, ReceiveMode.ReceiveAndDelete, retryPolicy);

				//Create a new Block - AzureServiceBus broker will receive a message from EventAggregator and publish to queue
				var expectedBlockId = tester.Explorer.CreateRPCClient().Generate(1)[0];

				msg = await messageReceiver.ReceiveAsync();

				JsonSerializerSettings settings = new JsonSerializerSettings();
				new Serializer(tester.Client.Network).ConfigureSerializer(settings);

				Assert.True(msg != null, $"No message received on Azure Service Bus Block Queue : {AzureServiceBusTestConfig.NewBlockQueue} after 10 read attempts.");

				Assert.Equal(msg.ContentType, typeof(NewBlockEvent).ToString());

				var blockEventQ = JsonConvert.DeserializeObject<NewBlockEvent>(Encoding.UTF8.GetString(msg.Body), settings);
				Assert.IsType<Models.NewBlockEvent>(blockEventQ);
				Assert.Equal(expectedBlockId.ToString().ToUpperInvariant(), msg.MessageId.ToUpperInvariant());
				Assert.Equal(expectedBlockId, blockEventQ.Hash);
				Assert.NotEqual(0, blockEventQ.Height);

				await Task.Delay(1000);
				Assert.True(busMessages.Count > 0, $"No message received on Azure Service Bus Block Topic : {AzureServiceBusTestConfig.NewBlockTopic}.");
				Microsoft.Azure.ServiceBus.Message busMsg = null;
				busMessages.TryPop(out busMsg);
				var blockEventS = JsonConvert.DeserializeObject<Models.NewBlockEvent>(Encoding.UTF8.GetString(busMsg.Body), settings);
				Assert.IsType<Models.NewBlockEvent>(blockEventS);
				Assert.Equal(expectedBlockId.ToString().ToUpperInvariant(), busMsg.MessageId.ToUpperInvariant());
				Assert.Equal(expectedBlockId, blockEventS.Hash);
				Assert.NotEqual(0, blockEventS.Height);
			}
		}

		[Fact]
		[Trait("Azure", "Azure")]
		public async Task CanSendAzureServiceBusNewTransactionEventMessage()
		{
			Assert.False(string.IsNullOrWhiteSpace(AzureServiceBusTestConfig.ConnectionString), "Please Set Azure Service Bus Connection string in TestConfig.cs AzureServiceBusTestConfig Class.");

			Assert.False(string.IsNullOrWhiteSpace(AzureServiceBusTestConfig.NewTransactionQueue), "Please Set Azure Service Bus NewTransactionQueue name in TestConfig.cs AzureServiceBusTestConfig Class.");

			Assert.False(string.IsNullOrWhiteSpace(AzureServiceBusTestConfig.NewTransactionSubscription), "Please Set Azure Service Bus NewTransactionSubscription name in TestConfig.cs AzureServiceBusTestConfig Class.");


			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), true);
				tester.Client.Track(pubkey);

				IQueueClient tranClient = new QueueClient(AzureServiceBusTestConfig.ConnectionString, AzureServiceBusTestConfig.NewTransactionQueue);
				ISubscriptionClient subscriptionClient = new SubscriptionClient(AzureServiceBusTestConfig.ConnectionString, AzureServiceBusTestConfig.NewTransactionTopic, AzureServiceBusTestConfig.NewTransactionSubscription);

				//Configure Service Bus Subscription callback				
				//We may have existing messages from other tests - push all message to a LIFO stack
				var busMessages = new ConcurrentStack<Microsoft.Azure.ServiceBus.Message>();

				var messageHandlerOptions = new MessageHandlerOptions((e) =>
				{
					throw e.Exception;
				})
				{
					// Maximum number of Concurrent calls to the callback `ProcessMessagesAsync`, set to 1 for simplicity.
					// Set it according to how many messages the application wants to process in parallel.
					MaxConcurrentCalls = 1,

					// Indicates whether MessagePump should automatically complete the messages after returning from User Callback.
					// False below indicates the Complete will be handled by the User Callback as in `ProcessMessagesAsync` below.
					AutoComplete = false
				};

				//Service Bus Topic Message Handler
				subscriptionClient.RegisterMessageHandler(async (m, t) =>
				{
					busMessages.Push(m);
					await subscriptionClient.CompleteAsync(m.SystemProperties.LockToken);
				}, messageHandlerOptions);

				//Test Service Bus Queue
				//Retry 10 times 
				var retryPolicy = new RetryExponential(new TimeSpan(0, 0, 0, 0, 500), new TimeSpan(0, 0, 1), 10);

				//Setup Message Receiver and clear queue
				var messageReceiver = new MessageReceiver(AzureServiceBusTestConfig.ConnectionString, AzureServiceBusTestConfig.NewTransactionQueue, ReceiveMode.ReceiveAndDelete, retryPolicy);
				while (await messageReceiver.PeekAsync() != null)
				{
					// Batch the receive operation
					var brokeredMessages = await messageReceiver.ReceiveAsync(300);
				}
				await messageReceiver.CloseAsync();


				//New message receiver to listen to our test event
				messageReceiver = new MessageReceiver(AzureServiceBusTestConfig.ConnectionString, AzureServiceBusTestConfig.NewTransactionQueue, ReceiveMode.ReceiveAndDelete, retryPolicy);

				//Create a new UTXO for our tracked key
				tester.SendToAddress(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

				//Check Queue Message
				Microsoft.Azure.ServiceBus.Message msg = null;
				msg = await messageReceiver.ReceiveAsync();

				Assert.True(msg != null, $"No message received on Azure Service Bus Transaction Queue : {AzureServiceBusTestConfig.NewTransactionQueue} after 10 read attempts.");

				var isCrptoCodeExist = msg.UserProperties.TryGetValue("CryptoCode", out object cryptoCode);
				Assert.True(isCrptoCodeExist, "No crypto code information in user properties.");
				Assert.Equal(tester.Client.Network.CryptoCode, (string)cryptoCode);

				//Configure JSON custom serialization
				NBXplorerNetwork networkForDeserializion = new NBXplorerNetworkProvider(ChainName.Regtest).GetFromCryptoCode((string)cryptoCode);
				JsonSerializerSettings settings = new JsonSerializerSettings();
				new Serializer(networkForDeserializion).ConfigureSerializer(settings);

				var txEventQ = JsonConvert.DeserializeObject<NewTransactionEvent>(Encoding.UTF8.GetString(msg.Body), settings);
				Assert.Equal(txEventQ.DerivationStrategy, pubkey);

				await Task.Delay(1000);
				Assert.True(busMessages.Count > 0, $"No message received on Azure Service Bus Transaction Topic : {AzureServiceBusTestConfig.NewTransactionTopic}.");
				//Check Service Bus Topic Payload

				Microsoft.Azure.ServiceBus.Message busMsg = null;
				busMessages.TryPop(out busMsg);
				var blockEventS = JsonConvert.DeserializeObject<NewTransactionEvent>(Encoding.UTF8.GetString(busMsg.Body), settings);
				Assert.IsType<NewTransactionEvent>(blockEventS);
				Assert.Equal(blockEventS.DerivationStrategy, pubkey);

			}
		}

		[Fact]
		[Trait("Broker", "RabbitMq")]
		public async Task CanSendRabbitMqNewTransactionEventMessage()
		{
			using (var tester = ServerTester.CreateNoAutoStart())
			{
				tester.UseRabbitMQ = true;
				tester.Start();
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), true);
				tester.Client.Track(pubkey);

				// RabbitMq connection
				var factory = new ConnectionFactory()
				{
					HostName = RabbitMqTestConfig.RabbitMqHostName,
					VirtualHost = RabbitMqTestConfig.RabbitMqVirtualHost,
					UserName = RabbitMqTestConfig.RabbitMqUsername,
					Password = RabbitMqTestConfig.RabbitMqPassword
				};
				IConnection connection = factory.CreateConnection();
				var channel = connection.CreateModel();
				channel.ExchangeDeclare(RabbitMqTestConfig.RabbitMqTransactionExchange, ExchangeType.Topic);

				// Setup a queue for all transactions
				var allTransactionsQueue = "allTransactions";
				var allTransactionsRoutingKey = $"transactions.#";
				channel.QueueDeclare(allTransactionsQueue, true, false, false);
				channel.QueueBind(allTransactionsQueue, RabbitMqTestConfig.RabbitMqTransactionExchange, allTransactionsRoutingKey);
				while (channel.BasicGet(allTransactionsQueue, true) != null) { } // Empty the queue

				// Setup a queue for all [CryptoCode] transactions
				var allBtcTransactionsQueue = "allBtcTransactions";
				var allBtcTransactionsRoutingKey = $"transactions.{tester.Client.Network.CryptoCode}.#";
				channel.QueueDeclare(allBtcTransactionsQueue, true, false, false);
				channel.QueueBind(allBtcTransactionsQueue, RabbitMqTestConfig.RabbitMqTransactionExchange, allBtcTransactionsRoutingKey);
				while (channel.BasicGet(allBtcTransactionsQueue, true) != null) { }

				// Setup a queue for all unconfirmed transactions
				var allUnConfirmedTransactionsQueue = "allUnConfirmedTransactions";
				var allUnConfirmedTransactionsRoutingKey = $"transactions.*.unconfirmed";
				channel.QueueDeclare(allUnConfirmedTransactionsQueue, true, false, false);
				channel.QueueBind(allUnConfirmedTransactionsQueue, RabbitMqTestConfig.RabbitMqTransactionExchange, allUnConfirmedTransactionsRoutingKey);
				while (channel.BasicGet(allUnConfirmedTransactionsQueue, true) != null) { }

				//Create a new UTXO for our tracked key
				tester.SendToAddress(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

				await Task.Delay(5000);

				BasicGetResult result = channel.BasicGet(allTransactionsQueue, true);
				Assert.True(result != null, $"No message received from RabbitMq Queue : {allTransactionsQueue}.");
				result = channel.BasicGet(allBtcTransactionsQueue, true);
				Assert.True(result != null, $"No message received from RabbitMq Queue : {allBtcTransactionsQueue}.");
				result = channel.BasicGet(allUnConfirmedTransactionsQueue, true);
				Assert.True(result != null, $"No message received from RabbitMq Queue : {allUnConfirmedTransactionsQueue}.");

				var isCrptoCodeExist = result.BasicProperties.Headers.TryGetValue("CryptoCode", out object cryptoCodeValue);
				Assert.True(isCrptoCodeExist, "No crypto code information in user properties.");

				var cryptoCode = Encoding.UTF8.GetString((cryptoCodeValue as byte[]));
				Assert.Equal(tester.Client.Network.CryptoCode, cryptoCode);

				var contentType = result.BasicProperties.ContentType;
				Assert.Equal(typeof(NewTransactionEvent).ToString(), contentType);

				var message = Encoding.UTF8.GetString(result.Body);

				//Configure JSON custom serialization
				NBXplorerNetwork networkForDeserializion = new NBXplorerNetworkProvider(ChainName.Regtest).GetFromCryptoCode((string)cryptoCode);
				JsonSerializerSettings settings = new JsonSerializerSettings();
				new Serializer(networkForDeserializion).ConfigureSerializer(settings);

				var txEventQ = JsonConvert.DeserializeObject<NewTransactionEvent>(message, settings);
				Assert.Equal(txEventQ.DerivationStrategy, pubkey);

				// Setup a queue for all confirmed transactions
				var allConfirmedTransactionsQueue = "allConfirmedTransactions";
				var allConfirmedTransactionsRoutingKey = $"transactions.*.confirmed";
				channel.QueueDeclare(allConfirmedTransactionsQueue, true, false, false);
				channel.QueueBind(allConfirmedTransactionsQueue, RabbitMqTestConfig.RabbitMqTransactionExchange, allConfirmedTransactionsRoutingKey);
				while (channel.BasicGet(allConfirmedTransactionsQueue, true) != null) { }

				tester.RPC.EnsureGenerate(1);
				await Task.Delay(5000);

				result = channel.BasicGet(allConfirmedTransactionsQueue, true);
				Assert.True(result != null, $"No message received from RabbitMq Queue : {allConfirmedTransactionsQueue}.");
			}
		}

		[Fact]
		[Trait("Broker", "RabbitMq")]
		public async Task CanSendRabbitMqNewBlockEventMessage()
		{
			using (var tester = ServerTester.CreateNoAutoStart())
			{
				tester.UseRabbitMQ = true;
				tester.Start();
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), true);
				tester.Client.Track(pubkey);

				// RabbitMq connection
				var factory = new ConnectionFactory()
				{
					HostName = RabbitMqTestConfig.RabbitMqHostName,
					VirtualHost = RabbitMqTestConfig.RabbitMqVirtualHost,
					UserName = RabbitMqTestConfig.RabbitMqUsername,
					Password = RabbitMqTestConfig.RabbitMqPassword
				};
				IConnection connection = factory.CreateConnection();
				var channel = connection.CreateModel();
				channel.ExchangeDeclare(RabbitMqTestConfig.RabbitMqBlockExchange, ExchangeType.Topic);

				// Setup a queue for all blocks
				var allBlocksQueue = "allBlocks";
				var allBlocksRoutingKey = $"blocks.#";
				channel.QueueDeclare(allBlocksQueue, true, false, false);
				channel.QueueBind(allBlocksQueue, RabbitMqTestConfig.RabbitMqBlockExchange, allBlocksRoutingKey);
				while (channel.BasicGet(allBlocksQueue, true) != null) { } // Empty the queue

				// Setup a queue for all [CryptoCode] blocks
				var allBtcBlocksQueue = "allBtcblocks";
				var allBtcBlocksRoutingKey = $"blocks.{tester.Client.Network.CryptoCode}";
				channel.QueueDeclare(allBtcBlocksQueue, true, false, false);
				channel.QueueBind(allBtcBlocksQueue, RabbitMqTestConfig.RabbitMqBlockExchange, allBtcBlocksRoutingKey);
				while (channel.BasicGet(allBtcBlocksQueue, true) != null) { }

				var expectedBlockId = tester.Explorer.CreateRPCClient().Generate(1)[0];
				await Task.Delay(5000);

				BasicGetResult result = channel.BasicGet(allBlocksQueue, true);
				Assert.True(result != null, $"No message received from RabbitMq Queue : {allBlocksQueue}.");
				result = channel.BasicGet(allBtcBlocksQueue, true);
				Assert.True(result != null, $"No message received from RabbitMq Queue : {allBtcBlocksQueue}.");

				var isCrptoCodeExist = result.BasicProperties.Headers.TryGetValue("CryptoCode", out object cryptoCodeValue);
				Assert.True(isCrptoCodeExist, "No crypto code information in user properties.");

				var cryptoCode = Encoding.UTF8.GetString((cryptoCodeValue as byte[]));
				Assert.Equal(tester.Client.Network.CryptoCode, cryptoCode);

				var contentType = result.BasicProperties.ContentType;
				Assert.Equal(typeof(NewBlockEvent).ToString(), contentType);

				var message = Encoding.UTF8.GetString(result.Body);

				//Configure JSON custom serialization
				NBXplorerNetwork networkForDeserializion = new NBXplorerNetworkProvider(ChainName.Regtest).GetFromCryptoCode((string)cryptoCode);
				JsonSerializerSettings settings = new JsonSerializerSettings();
				new Serializer(networkForDeserializion).ConfigureSerializer(settings);

				var blockEventQ = JsonConvert.DeserializeObject<NewBlockEvent>(message, settings);
				Assert.IsType<Models.NewBlockEvent>(blockEventQ);
				Assert.Equal(expectedBlockId.ToString().ToUpperInvariant(), result.BasicProperties.MessageId.ToUpperInvariant());
				Assert.Equal(expectedBlockId, blockEventQ.Hash);
				Assert.NotEqual(0, blockEventQ.Height);
			}
		}

		class TestMetadata
		{
			public string Message { get; set; }
		}
		[Fact]
		public void CanTrimEvents()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var ids = tester.Explorer.Generate(100);
				var session = tester.Client.CreateLongPollingNotificationSession(0);
				session.WaitForBlocks(ids);
				var allEvents = session.GetEvents();
				tester.TrimEvents = 15;
				tester.ResetExplorer(false);
				tester.Client.WaitServerStarted();
				session = tester.Client.CreateLongPollingNotificationSession(0);
				allEvents = session.GetEvents();
				Assert.Equal(15, allEvents.Length);
				Assert.Contains(allEvents.OfType<NewBlockEvent>(), b => b.Hash == ids.Last());
				var highestEvent = allEvents.Last().EventId;
				ids = tester.Explorer.Generate(1);
				session = tester.Client.CreateLongPollingNotificationSession(0);
				session.WaitForBlocks(ids);
				allEvents = session.GetEvents();
				Assert.Equal(ids[0], Assert.IsType<NewBlockEvent>(allEvents.Last()).Hash);
			}
		}
		[FactWithTimeout]
		public async Task CanGetAndSetMetadata()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), true);
				await tester.Client.TrackAsync(pubkey);

				Assert.Null(await tester.Client.GetMetadataAsync<TestMetadata>(pubkey, "test"));
				Assert.Null(await tester.Client.GetMetadataAsync<TestMetadata>(pubkey, "test"));

				var expected = new TestMetadata() { Message = "hello" };
				await tester.Client.SetMetadataAsync(pubkey, "test", expected);

				var actual = await tester.Client.GetMetadataAsync<TestMetadata>(pubkey, "test");
				Assert.NotNull(actual);
				Assert.Equal(expected.Message, actual.Message);

				await tester.Client.SetMetadataAsync<TestMetadata>(pubkey, "test", null);
				Assert.Null(await tester.Client.GetMetadataAsync<TestMetadata>(pubkey, "test"));

				await tester.Client.SetMetadataAsync(pubkey, "test3", true);
				Assert.True(await tester.Client.GetMetadataAsync<bool>(pubkey, "test3"));
			}
		}

		PruneRequest PruneTheMost = new PruneRequest() { DaysToKeep = 0.0 };
		[FactWithTimeout]
		public async Task CanPrune()
		{
			// In this test we have fundingTxId with 2 output and spending1
			// We make sure that only once the 2 outputs of fundingTxId have been consumed
			// fundingTxId get pruned
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), true);
				await tester.Client.TrackAsync(pubkey);
				var fundingTxId = new uint256((await tester.RPC.SendCommandAsync(RPCOperations.sendmany, "",
						JObject.Parse($"{{ \"{tester.AddressOf(pubkey, "0/1")}\": \"0.9\", \"{tester.AddressOf(pubkey, "0/0")}\": \"0.5\" }}"))).ResultString);
				tester.Notifications.WaitForTransaction(pubkey, fundingTxId);
				var utxo = await tester.Client.GetUTXOsAsync(pubkey);
				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForBlocks(tester.RPC.Generate(1));
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Equal(2, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(fundingTxId, utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Logs.Tester.LogInformation($"Funding tx ({fundingTxId}) has two coins");
				Logs.Tester.LogInformation("Let's spend one of the coins");
				LockTestCoins(tester.RPC);
				await tester.ImportPrivKeyAsync(key, "0/1");

				var spending1 = await tester.RPC.SendToAddressAsync(new Key().PubKey.Hash.GetAddress(tester.Network), Money.Coins(0.1m));
				tester.Notifications.WaitForTransaction(pubkey, spending1);
				Logs.Tester.LogInformation($"Spent on {spending1}");
				tester.RPC.EnsureGenerate(1);
				tester.WaitSynchronized();
				await tester.Client.PruneAsync(pubkey, PruneTheMost);

				Logs.Tester.LogInformation("It still should not pruned, because there is still another UTXO in funding tx");
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				AssertNotPruned(tester, pubkey, fundingTxId);
				AssertNotPruned(tester, pubkey, spending1);

				Logs.Tester.LogInformation("Let's spend the other coin");
				LockTestCoins(tester.RPC);
				tester.ImportPrivKey(key, "0/0");
				var unspentt = tester.RPC.ListUnspent();
				var spending2 = tester.RPC.SendToAddress(new Key().PubKey.Hash.GetAddress(tester.Network), Money.Coins(0.1m));
				tester.Notifications.WaitForTransaction(pubkey, spending2);
				Logs.Tester.LogInformation($"Spent on {spending2}");

				tester.RPC.EnsureGenerate(3);
				tester.WaitSynchronized();
				Logs.Tester.LogInformation($"Now {spending1} and {spending2} should be pruned if we want to keep 1H of blocks");
				tester.Client.Prune(pubkey, new PruneRequest() { DaysToKeep = 1.0 / 24.0 });
				AssertNotPruned(tester, pubkey, fundingTxId);
				AssertNotPruned(tester, pubkey, spending1);
				AssertNotPruned(tester, pubkey, spending2);

				tester.RPC.Generate(4);
				tester.WaitSynchronized();
				var totalPruned = tester.Client.Prune(pubkey, new PruneRequest() { DaysToKeep = 1.0 / 24.0 }).TotalPruned;
				Assert.Equal(3, totalPruned);
				totalPruned = tester.Client.Prune(pubkey, new PruneRequest() { DaysToKeep = 1.0 / 24.0 }).TotalPruned;
				Assert.Equal(0, totalPruned);
				Logs.Tester.LogInformation($"But after 1H of blocks, it should be pruned");
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				AssertPruned(tester, pubkey, fundingTxId);
				AssertPruned(tester, pubkey, spending1);
				AssertPruned(tester, pubkey, spending2);
			}
		}

		private static void AssertPruned(ServerTester tester, DerivationStrategyBase pubkey, uint256 txid)
		{
			var txs = tester.Client.GetTransactions(pubkey);
			Assert.DoesNotContain(txid, txs.ConfirmedTransactions.Transactions.Select(t => t.TransactionId).ToArray());
		}
		private static void AssertNotPruned(ServerTester tester, DerivationStrategyBase pubkey, uint256 txid)
		{
			var txs = tester.Client.GetTransactions(pubkey);
			Assert.Contains(txid, txs.ConfirmedTransactions.Transactions.Select(t => t.TransactionId).ToArray());
		}

		private static TransactionInformation AssertExist(ServerTester tester, DerivationStrategyBase pubkey, uint256 txid, bool shouldBePruned)
		{
			return AssertExist(tester, new DerivationSchemeTrackedSource(pubkey), txid, shouldBePruned);
		}
		private static TransactionInformation AssertExist(ServerTester tester, TrackedSource pubkey, uint256 txid, bool shouldBePruned)
		{
			int retry = 0;
			TransactionInformation tx = null;
			while (true)
			{
				retry++;
				var txs = tester.Client.GetTransactions(pubkey);
				tx = txs.ConfirmedTransactions.Transactions.Where(t => t.TransactionId == txid).FirstOrDefault();
				if (tx == null && retry < 10)
				{
					Thread.Sleep(200);
					continue;
				}
				if (tx != null)
					break;
				Assert.Fail($"Transaction {txid} should exists");
			}
			if (shouldBePruned && tx.Transaction != null)
				Assert.Fail($"Transaction {txid} should be pruned");
			if (!shouldBePruned && tx.Transaction == null)
				Assert.Fail($"Transaction {txid} should not be pruned");
			return tx;
		}

		[FactWithTimeout]
		public async Task CanPrune2()
		{
			// In this test we have fundingTxId with 2 output and spending1
			// We make sure that if only 1 outputs of fundingTxId have been consumed
			// spending1 does not get pruned, even if its output got consumed
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), true);
				await tester.Client.TrackAsync(pubkey);

				var utxo = await tester.Client.GetUTXOsAsync(pubkey);

				tester.RPC.SendCommand(RPCOperations.sendmany, "",
						JObject.Parse($"{{ \"{tester.AddressOf(pubkey, "0/1")}\": \"0.9\", \"{tester.AddressOf(pubkey, "0/0")}\": \"0.5\" }}"));
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Equal(2, utxo.Confirmed.UTXOs.Count);
				var fundingTxId = utxo.Confirmed.UTXOs[0].Outpoint.Hash;
				Logs.Tester.LogInformation($"Sent funding tx fundingTx({fundingTxId}) to 0/1 and 0/0");

				// Let's spend one of the coins of funding and spend it again
				// [funding, spending1, spending2]
				LockTestCoins(tester.RPC);
				tester.ImportPrivKey(key, "0/1");
				var coinDestination = tester.Client.GetUnused(pubkey, DerivationFeature.Deposit);
				var coinDestinationAddress = coinDestination.ScriptPubKey;
				var spending1 = tester.RPC.SendToAddress(coinDestinationAddress, Money.Coins(0.1m));
				Logs.Tester.LogInformation($"Spent the coin to 0/1 in spending1({spending1})");
				tester.Notifications.WaitForTransaction(pubkey, spending1);
				LockTestCoins(tester.RPC, new HashSet<Script>());
				tester.ImportPrivKey(key, coinDestination.KeyPath.ToString());
				var spending2 = tester.RPC.SendToAddress(new Key().GetScriptPubKey(ScriptPubKeyType.Legacy), Money.Coins(0.01m));
				tester.Notifications.WaitForTransaction(pubkey, spending2);
				Logs.Tester.LogInformation($"Spent again the coin in spending2({spending2})");
				var tx = tester.RPC.GetRawTransactionAsync(spending2).Result;
				Assert.Contains(tx.Inputs, (i) => i.PrevOut.Hash == spending1);
				tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));
				tester.WaitSynchronized();

				await tester.Client.PruneAsync(pubkey, PruneTheMost);
				// spending1 should not be pruned because fundingTx still can't be pruned
				Logs.Tester.LogInformation($"Spending spending1({spending1}) and spending2({spending2} can't be pruned, because a common ancestor fundingTx({fundingTxId}) can't be pruned");
				utxo = tester.Client.GetUTXOs(pubkey);
				AssertNotPruned(tester, pubkey, fundingTxId);
				AssertNotPruned(tester, pubkey, spending1);
				AssertNotPruned(tester, pubkey, spending2);

				// Let's spend the other coin of fundingTx
				Thread.Sleep(1000);
				LockTestCoins(tester.RPC, new HashSet<Script>());
				tester.ImportPrivKey(key, "0/0");
				var spending3 = tester.RPC.SendToAddress(new Key().PubKey.Hash.GetAddress(tester.Network), Money.Coins(0.1m));
				tester.Notifications.WaitForTransaction(pubkey, spending3);
				Logs.Tester.LogInformation($"Spent the second coin to 0/0 in spending3({spending3})");
				tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));
				tester.WaitSynchronized();
				await tester.Client.PruneAsync(pubkey, PruneTheMost);

				Logs.Tester.LogInformation($"Now fundingTx({fundingTxId}), spendgin1({spending1}) and spending2({spending2}) should be pruned");
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				AssertPruned(tester, pubkey, fundingTxId);
				AssertPruned(tester, pubkey, spending1);
				AssertPruned(tester, pubkey, spending2);
			}
		}

		[FactWithTimeout]
		public async Task CanUseWebSockets()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), true);
				await tester.Client.TrackAsync(pubkey);
				using (var connected = tester.Client.CreateWebsocketNotificationSession())
				{
					connected.ListenNewBlock();
					var expectedBlockId = tester.Explorer.CreateRPCClient().Generate(1)[0];
					var blockEvent = (Models.NewBlockEvent)connected.NextEvent(Cancel);
					// Sometimes Postgres backend emit one more block during warmup. That's not a bug,
					// but make test flaky.
					if (blockEvent.Hash != expectedBlockId)
						blockEvent = (Models.NewBlockEvent)connected.NextEvent(Cancel);
					
					Assert.True(blockEvent.EventId != 0);
					Assert.Equal(expectedBlockId, blockEvent.Hash);
					Assert.NotEqual(0, blockEvent.Height);
					
					Assert.Equal(1, blockEvent.Confirmations);

					connected.ListenDerivationSchemes(new[] { pubkey });
					await tester.SendToAddressAsync(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}

				using (var connected = tester.Client.CreateWebsocketNotificationSession())
				{
					connected.ListenAllDerivationSchemes();
					await tester.SendToAddressAsync(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}

				using (var connected = tester.Client.CreateWebsocketNotificationSession())
				{
					connected.ListenAllTrackedSource();
					await tester.SendToAddressAsync(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}
			}
		}

		[FactWithTimeout]
		public async Task CanUseLongPollingNotifications()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), true);
				await tester.Client.TrackAsync(pubkey);
				var connected = tester.Client.CreateLongPollingNotificationSession();
				{
					var expectedBlockId = tester.Explorer.CreateRPCClient().Generate(1)[0];
					var blockEvent = (Models.NewBlockEvent)connected.NextEvent(Cancel);
					// With the postgres backend, when the indexer starts first time, it asks
					// for the blocks previous to the highest block. So we get one more event.
					// This is harmless.
					blockEvent = (Models.NewBlockEvent)connected.NextEvent(Cancel);
					Assert.Equal(expectedBlockId, blockEvent.Hash);
					Assert.NotEqual(0, blockEvent.Height);
					await tester.SendToAddressAsync(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}

				connected = tester.Client.CreateLongPollingNotificationSession(connected.LastEventId);
				{
					await tester.SendToAddressAsync(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}

				connected = tester.Client.CreateLongPollingNotificationSession(connected.LastEventId);
				{
					await tester.SendToAddressAsync(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}
			}
		}

		[FactWithTimeout]
		public async Task CanUseWebSockets2()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				
				var wLegacy = await tester.Client.GenerateWalletAsync(new GenerateWalletRequest() { ScriptPubKeyType = ScriptPubKeyType.Legacy });
				var wSegwit = await tester.Client.GenerateWalletAsync(new GenerateWalletRequest() { ScriptPubKeyType = ScriptPubKeyType.Segwit });

				(var pubkey, var pubkey2) = (wLegacy.DerivationScheme, wSegwit.DerivationScheme);

				using (var connected = tester.Client.CreateWebsocketNotificationSession())
				{
					connected.ListenAllDerivationSchemes();
					tester.Explorer.CreateRPCClient().SendCommand(RPCOperations.sendmany, "",
						JObject.Parse($"{{ \"{tester.AddressOf(pubkey, "0/1")}\": \"0.9\", \"{tester.AddressOf(pubkey, "1/1")}\": \"0.5\"," +
										$"\"{tester.AddressOf(pubkey2, "0/2")}\": \"0.9\", \"{tester.AddressOf(pubkey2, "1/2")}\": \"0.5\" }}"));

					var schemes = new[] { pubkey.ToString(), pubkey2.ToString() }.ToList();

					int expectedOutput = tester.RPC.Capabilities.SupportSegwit ? 2 : 4; // if does not support segwit pubkey == pubkey2
					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(expectedOutput, txEvent.Outputs.Count);
					foreach (var output in txEvent.Outputs)
					{
						var txOut = txEvent.TransactionData.Transaction.Outputs[output.Index];
						Assert.Equal(txOut.ScriptPubKey, output.ScriptPubKey);
						Assert.Equal(txOut.Value, output.Value);
						var derived = ((DerivationSchemeTrackedSource)txEvent.TrackedSource).DerivationStrategy.GetDerivation(output.KeyPath);
						Assert.Equal(derived.ScriptPubKey, txOut.ScriptPubKey);
					}
					var fundingTx = txEvent.TransactionData.TransactionHash;
					Assert.Contains(txEvent.DerivationStrategy.ToString(), schemes);
					schemes.Remove(txEvent.DerivationStrategy.ToString());

					if (!tester.RPC.Capabilities.SupportSegwit)
						return;

					txEvent = (Models.NewTransactionEvent)await connected.NextEventAsync(Cancel);
					Assert.Equal(2, txEvent.Outputs.Count);
					Assert.Contains(txEvent.DerivationStrategy.ToString(), new[] { pubkey.ToString(), pubkey2.ToString() });
					Assert.Empty(txEvent.Inputs);

					// Here, we will try to spend the coins of the segwit wallet
					var psbt = await tester.Client.CreatePSBTAsync(pubkey2, new CreatePSBTRequest()
					{ 
						Destinations = [
							new ()
							{
								SweepAll = true,
								Destination = tester.AddressOf(pubkey, "1/1")
							},
						],
						FeePreference = new FeePreference() { ExplicitFee = Money.Satoshis(1000) }
					});
					var signed = psbt.PSBT.SignAll(ScriptPubKeyType.Segwit, wSegwit.AccountHDKey, wSegwit.AccountKeyPath).Finalize().ExtractTransaction();
					await tester.Client.BroadcastAsync(signed);

					// Make sure we receive two events with expected inputs/outputs
					for (int evtidx = 0; evtidx < 2; evtidx++)
					{
						txEvent = (Models.NewTransactionEvent)await connected.NextEventAsync(Cancel);
						if (txEvent.TrackedSource == TrackedSource.Parse(wSegwit.TrackedSource, tester.NBXplorerNetwork))
						{
							
							void AssertInputs(List<MatchedInput> inputs)
							{
								Assert.Equal(2, inputs.Count);
								for (int i = 0; i < inputs.Count; i++)
								{
									var input = txEvent.Inputs[i];
									Assert.Equal(fundingTx, input.TransactionId);
									Assert.Equal(i, input.InputIndex);
									if (input.KeyPath == new KeyPath("0/2"))
									{
										Assert.Equal(Money.Coins(0.9m), input.Value);
										Assert.Equal(tester.AddressOf(pubkey2, "0/2"), input.Address);
										Assert.Equal(input.Address.ScriptPubKey, input.ScriptPubKey);
									}
									else if (input.KeyPath == new KeyPath("1/2"))
									{
										Assert.Equal(Money.Coins(0.5m), input.Value);
										Assert.Equal(tester.AddressOf(pubkey2, "1/2"), input.Address);
										Assert.Equal(input.Address.ScriptPubKey, input.ScriptPubKey);
									}
									else
										Assert.Fail("Unknown keypath " + input.KeyPath);
								}
							}
							Assert.Empty(txEvent.Outputs);
							AssertInputs(txEvent.Inputs);
							var tx = await tester.Client.GetTransactionAsync(txEvent.TrackedSource, txEvent.TransactionData.TransactionHash);
							Assert.Empty(tx.Outputs);
							AssertInputs(tx.Inputs);
						}
						else if (txEvent.TrackedSource == TrackedSource.Parse(wLegacy.TrackedSource, tester.NBXplorerNetwork))
						{
							Assert.Empty(txEvent.Inputs);
							Assert.Single(txEvent.Outputs);
						}
						else
							Assert.Fail("Should not be reached");
					}
				}
			}
		}

		[FactWithTimeout]
		public async Task DoNotLoseTimestampForLongConfirmations()
		{
			using (var tester = ServerTester.Create())
			{
				var bob = new BitcoinExtKey(new ExtKey(), tester.Network);
				var bobPubKey = tester.CreateDerivationStrategy(bob.Neuter());
				tester.Client.Track(bobPubKey);
				var id = tester.SendToAddress(tester.AddressOf(bob, "0/1"), Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(bobPubKey, id);
				var repo = tester.GetService<RepositoryProvider>().GetRepository(tester.Network.NetworkSet.CryptoCode);
				var transactions = await repo.GetTransactions(new DerivationSchemeTrackedSource(bobPubKey), id);
				var tx = Assert.Single(transactions);
				var timestamp = tx.FirstSeen;
				var match = (await repo.GetMatches(tx.Transaction, null, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2), false));
				await repo.SaveMatches(match);
				transactions = await repo.GetTransactions(new DerivationSchemeTrackedSource(bobPubKey), id);
				tx = Assert.Single(transactions);
				Assert.Equal(timestamp, tx.FirstSeen);
			}
		}

		[FactWithTimeout]
		public async Task CanTrack4()
		{
			using (var tester = ServerTester.Create())
			{
				var bob = new BitcoinExtKey(new ExtKey(), tester.Network);
				var alice = new BitcoinExtKey(new ExtKey(), tester.Network);

				var bobPubKey = tester.CreateDerivationStrategy(bob.Neuter());
				var alicePubKey = tester.CreateDerivationStrategy(alice.Neuter());

				await tester.Client.TrackAsync(alicePubKey);
				var utxoAlice = tester.Client.GetUTXOs(alicePubKey);
				await tester.Client.TrackAsync(bobPubKey);
				var utxoBob = tester.Client.GetUTXOs(bobPubKey);

				Logs.Tester.LogInformation("Let's send 1.0BTC to alice 0/1 and 0.1BTC to bob 0/2 then mine");
				var id = tester.SendToAddress(tester.AddressOf(alice, "0/1"), Money.Coins(1.0m));
				id = tester.SendToAddress(tester.AddressOf(bob, "0/2"), Money.Coins(0.1m));
				tester.Notifications.WaitForTransaction(bobPubKey, id);
				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForTransaction(bobPubKey, id);

				utxoAlice = tester.Client.GetUTXOs(alicePubKey);
				utxoBob = tester.Client.GetUTXOs(bobPubKey);

				Logs.Tester.LogInformation("Let's send 0.6BTC from alice 0/1 to bob 0/3");
				LockTestCoins(tester.RPC);
				tester.ImportPrivKey(alice, "0/1");
				id = tester.SendToAddress(tester.AddressOf(bob, "0/3"), Money.Coins(0.6m));
				tester.Notifications.WaitForTransaction(bobPubKey, id);

				utxoAlice = tester.Client.GetUTXOs(alicePubKey);
				utxoBob = tester.Client.GetUTXOs(bobPubKey);

				Logs.Tester.LogInformation("Let's check Alice spent her confirmed UTXO and Bob got his 0.6BTC");
				Assert.Single(utxoAlice.Confirmed.UTXOs);
				Assert.Single(utxoAlice.Unconfirmed.SpentOutpoints);
				Assert.Equal(utxoAlice.Unconfirmed.SpentOutpoints[0], utxoAlice.Confirmed.UTXOs[0].Outpoint);

				Assert.Single(utxoBob.Confirmed.UTXOs);
				Assert.Equal(Money.Coins(0.1m), utxoBob.Confirmed.UTXOs[0].Value);
				Assert.Single(utxoBob.Unconfirmed.UTXOs);
				Assert.Empty(utxoBob.Unconfirmed.SpentOutpoints);
				Assert.Equal(Money.Coins(0.6m), utxoBob.Unconfirmed.UTXOs[0].Value);

				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForTransaction(bobPubKey, id);

				Logs.Tester.LogInformation("Let's check bob own 0/6BTC and 0.1 BTC, while Alice own nothing (no change)");
				utxoAlice = tester.Client.GetUTXOs(alicePubKey);
				utxoBob = tester.Client.GetUTXOs(bobPubKey);

				Assert.Empty(utxoAlice.Confirmed.UTXOs);
				Assert.Empty(utxoBob.Confirmed.SpentOutpoints);
				Assert.Equal(2, utxoBob.Confirmed.UTXOs.Count);
				Assert.Contains(utxoBob.Confirmed.UTXOs.Select(u => u.KeyPath.ToString()), o => o == "0/2");
				Assert.Contains(utxoBob.Confirmed.UTXOs.Select(u => u.KeyPath.ToString()), o => o == "0/3");
			}
		}

		[FactWithTimeout]
		public async Task CanTrack3()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				await tester.Client.TrackAsync(pubkey);
				var events = tester.Client.CreateWebsocketNotificationSession();
				events.ListenDerivationSchemes(new[] { pubkey });

				Logs.Tester.LogInformation("Let's send to 0/0, 0/1, 0/2, 0, 1");
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
				var utxo = await tester.Client.GetUTXOsAsync(pubkey);

				tester.RPC.EnsureGenerate(1);
				events.NextEvent(Timeout);
				events.NextEvent(Timeout);
				events.NextEvent(Timeout);
				events.NextEvent(Timeout);
				events.NextEvent(Timeout);

				Logs.Tester.LogInformation("Did we received 5 UTXOs?");
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Equal(5, utxo.Confirmed.UTXOs.Count);

				var psbt = (await tester.Client.CreatePSBTAsync(pubkey, new CreatePSBTRequest()
				{
					Destinations = new List<CreatePSBTDestination>()
					{
						new CreatePSBTDestination()
						{
							Amount = Money.Coins(5m),
							Destination = new Key().GetAddress(ScriptPubKeyType.Legacy, tester.Network)
						}
					},
					FeePreference = new FeePreference() { ExplicitFee = Money.Satoshis(5000) }
				})).PSBT;
				Assert.Equal(5, psbt.Inputs.Count);
				psbt = psbt.SignAll(ScriptPubKeyType.Segwit, key);
				psbt.Finalize();
				var tx = psbt.ExtractTransaction();
				await tester.Client.BroadcastAsync(tx);
				tester.Notifications.WaitForTransaction(pubkey, tx.GetHash());
				var unconfTx = await tester.Client.GetTransactionAsync(pubkey, tx.GetHash());
				foreach (var i in Enumerable.Range(0, 5))
				{
					Assert.Contains(unconfTx.Inputs, input => input.InputIndex == i);
				}
			}
		}

		[FactWithTimeout]
		public async Task CanTrackSeveralTransactions()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				await tester.Client.TrackAsync(pubkey);

				var addresses = new HashSet<Script>();
				await tester.ImportPrivKeyAsync(key, "0/0");
				var id = await tester.SendToAddressAsync(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(pubkey, id);
				addresses.Add(tester.AddressOf(key, "0/0").ScriptPubKey);

				var utxo = await tester.Client.GetUTXOsAsync(pubkey);

				var coins = Money.Coins(1.0m);

				Logs.Tester.LogInformation($"Creating a chain of 20 unconfirmed transaction...");
				int i = 0;
				// Reserve addresses ahead of time so that we are sure that the server is not too late to generate the next one
				for (i = 0; i < 20; i++)
				{
					await tester.Client.GetUnusedAsync(pubkey, DerivationFeature.Deposit, reserve: true);
				}
				uint256 lastTx = null;
				for (i = 0; i < 20; i++)
				{
					LockTestCoins(tester.RPC, addresses);
					var spendable = await tester.RPC.ListUnspentAsync(0, 0);
					coins = coins - Money.Coins(0.001m);
					var path = $"0/{i + 1}";
					var destination = tester.AddressOf(key, path);
					await tester.ImportPrivKeyAsync(key, path);
					var txId = await tester.SendToAddressAsync(destination, coins);
					Logs.Tester.LogInformation($"Sent to {path} in {txId}");
					addresses.Add(destination.ScriptPubKey);
					lastTx = txId;
				}

				tester.Notifications.WaitForTransaction(pubkey, lastTx);
				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForTransaction(pubkey, lastTx);

				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(lastTx, utxo.Confirmed.UTXOs[0].TransactionHash);
			}
		}

		[FactWithTimeout]
		public async void CanUseWebSocketsOnAddress()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				await Task.Delay(500);
				var key = new Key();
				var pubkey = TrackedSource.Create(key.PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network));
				tester.Client.Track(pubkey);
				using (var connected = tester.Client.CreateWebsocketNotificationSession())
				{
					connected.ListenNewBlock();
					var expectedBlockId = tester.Explorer.CreateRPCClient().Generate(1)[0];
					var blockEvent = (Models.NewBlockEvent)connected.NextEvent(Cancel);
					Assert.Equal(expectedBlockId, blockEvent.Hash);
					Assert.NotEqual(0, blockEvent.Height);

					connected.ListenTrackedSources(new[] { pubkey });
					tester.SendToAddress(pubkey.Address, Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.NotEmpty(txEvent.Outputs);
					Assert.Equal(pubkey.Address.ScriptPubKey, txEvent.Outputs[0].ScriptPubKey);
					Assert.Equal(pubkey.Address, txEvent.Outputs[0].Address);
					Assert.Equal(txEvent.TrackedSource, pubkey);
				}

				using (var connected = tester.Client.CreateWebsocketNotificationSession())
				{
					connected.ListenAllTrackedSource();
					tester.SendToAddress(pubkey.Address, Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.TrackedSource, pubkey);
				}
			}
		}

		[FactWithTimeout]
		public async Task CanUseWebSocketsOnAddress2()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new Key();
				var pubkey = TrackedSource.Create(key.PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network));

				var key2 = new Key();
				var pubkey2 = TrackedSource.Create(key2.PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network));

				await tester.Client.TrackAsync(pubkey);
				await tester.Client.TrackAsync(pubkey2);
				using (var connected = tester.Client.CreateWebsocketNotificationSession())
				{
					connected.ListenAllTrackedSource();
					tester.Explorer.CreateRPCClient().SendCommand(RPCOperations.sendmany, "",
						JObject.Parse($"{{ \"{pubkey.Address}\": \"0.9\", \"{pubkey.Address}\": \"0.5\"," +
									  $"\"{pubkey2.Address}\": \"0.9\", \"{pubkey2.Address}\": \"0.5\" }}"));

					var trackedSources = new[] { pubkey.ToString(), pubkey2.ToString() }.ToList();

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.NotEmpty(txEvent.Outputs);
					Assert.Contains(txEvent.TrackedSource.ToString(), trackedSources);
					trackedSources.Remove(txEvent.TrackedSource.ToString());

					txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.NotEmpty(txEvent.Outputs);
					Assert.Contains(txEvent.TrackedSource.ToString(), new[] { pubkey.ToString(), pubkey2.ToString() });
				}
			}
		}

		[FactWithTimeout]
		public async Task CanTrackAddress()
		{
			using (var tester = ServerTester.Create())
			{
				var extkey = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.NBXplorerNetwork.DerivationStrategyFactory.Parse($"{extkey.Neuter()}-[legacy]");
				Logs.Tester.LogInformation("Let's make a tracked address from hd pubkey 0/0");
				var key = extkey.ExtKey.Derive(new KeyPath("0/0")).PrivateKey;
				var address = key.PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network);
				var addressSource = TrackedSource.Create(address);
				tester.Client.Track(addressSource);

				Logs.Tester.LogInformation("Let's send 0.1BTC to tracked address");
				var tx1 = tester.SendToAddress(address, Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(address, tx1);
				var utxo = await tester.Client.GetUTXOsAsync(addressSource);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(tx1, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);

				Logs.Tester.LogInformation("Let's make sure hd pubkey 0/0 is also tracked, even if we tracked it later");
				tester.Client.Track(pubkey);
				var unused = tester.Client.GetUnused(pubkey, DerivationFeature.Deposit);
				Assert.Equal(new KeyPath("0/1"), unused.KeyPath);
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Single(utxo.Unconfirmed.UTXOs);

				Logs.Tester.LogInformation("But this end up tracked once the block is mined");
				tester.RPC.Generate(1);
				tester.Notifications.WaitForTransaction(pubkey, tx1);
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(tx1, utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.NotNull(utxo.TrackedSource);
				Assert.NotNull(utxo.DerivationStrategy);
				var dsts = Assert.IsType<DerivationSchemeTrackedSource>(utxo.TrackedSource);
				Assert.Equal(utxo.DerivationStrategy, dsts.DerivationStrategy);

				Logs.Tester.LogInformation("Make sure the transaction appear for tracked address as well");
				utxo = await tester.Client.GetUTXOsAsync(addressSource);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(tx1, utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.NotNull(utxo.TrackedSource);
				Assert.Null(utxo.DerivationStrategy);
				Assert.IsType<AddressTrackedSource>(utxo.TrackedSource);

				Logs.Tester.LogInformation("Check it appear in transaction list");
				var tx = await tester.Client.GetTransactionsAsync(addressSource);
				Assert.Equal(tx1, tx.ConfirmedTransactions.Transactions[0].TransactionId);

				tx = await tester.Client.GetTransactionsAsync(pubkey);
				Assert.Equal(tx1, tx.ConfirmedTransactions.Transactions[0].TransactionId);

				Logs.Tester.LogInformation("Trying to send to a single address from a tracked extkey");
				var extkey2 = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey2 = tester.NBXplorerNetwork.DerivationStrategyFactory.Parse($"{extkey.Neuter()}-[legacy]");
				await tester.Client.TrackAsync(pubkey2);
				var txId = tester.SendToAddress(pubkey2.GetDerivation(new KeyPath("0/0")).ScriptPubKey, Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(pubkey2, txId);

				Logs.Tester.LogInformation("Sending from 0/0 to the tracked address");
				utxo = await tester.Client.GetUTXOsAsync(addressSource);
				var utxo2 = await tester.Client.GetUTXOsAsync(pubkey2);
				LockTestCoins(tester.RPC);
				tester.ImportPrivKey(extkey2, "0/0");
				var tx2 = tester.SendToAddress(address, Money.Coins(0.6m));
				tester.Notifications.WaitForTransaction(address, tx2);
				tester.RPC.EnsureGenerate(1);
				AssertExist(tester, addressSource, tx2, false);
				AssertExist(tester, pubkey2, tx2, false);
				utxo = await tester.Client.GetUTXOsAsync(addressSource);
				utxo2 = await tester.Client.GetUTXOsAsync(pubkey2);
				Assert.NotEmpty(utxo.Confirmed.UTXOs);
				Assert.NotEmpty(utxo2.Confirmed.UTXOs);
				Assert.Contains(utxo2.Confirmed.UTXOs, u => u.TransactionHash == tx2);
				Assert.Contains(utxo.Confirmed.UTXOs, u => u.TransactionHash == tx2);
				Assert.Null(utxo.Confirmed.UTXOs[0].Feature);
				Assert.NotNull(utxo2.Confirmed.UTXOs[0].Outpoint);
			}
		}

		[Fact]
		public async Task Test()
		{
			var rpc = new RPCClient(new RPCCredentialString()
			{
				UserPassword = new NetworkCredential("dashrpc","PQQgOzs1jN7q2SWQ6TpBNLm9j"),
			}, "https://dash-testnet.nodes.m3t4c0.xyz", AltNetworkSets.Dash.Testnet);
			var b1 = await rpc.GetBlockAsync(new uint256("000001f02c1623e0bb12b54ac505cefdfca3f0f664bf333fc73ae5eafe34b830"));
		}

		[FactWithTimeout]
		public async Task CanTrack2()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				await tester.Client.TrackAsync(pubkey);
				Logs.Tester.LogInformation("Let's send 1.0BTC to 0/0");
				var tx00 = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(pubkey, tx00);
				var utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(tx00, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);


				Logs.Tester.LogInformation("Let's send 0.6BTC from 0/0 to 1/0");
				LockTestCoins(tester.RPC);
				tester.ImportPrivKey(key, "0/0");
				var tx2 = tester.SendToAddress(tester.AddressOf(key, "1/0"), Money.Coins(0.6m));
				tester.Notifications.WaitForTransaction(pubkey, tx2);

				Logs.Tester.LogInformation("Should have 1 unconf UTXO of 0.6BTC");
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(tx2, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash); //got the 0.6m
				Assert.Equal(Money.Coins(0.6m), utxo.Unconfirmed.UTXOs[0].Value); //got the 0.6m
				Assert.Empty(utxo.Unconfirmed.SpentOutpoints);

				Logs.Tester.LogInformation("Let's send 0.15BTC to 0/0");
				var txid = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(0.15m));
				tester.Notifications.WaitForTransaction(pubkey, txid);

				Logs.Tester.LogInformation("0.15BTC and 0.6BTC should be in our UTXO");
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Equal(2, utxo.Unconfirmed.UTXOs.Count);
				Assert.IsType<Coin>(utxo.Unconfirmed.UTXOs[0].AsCoin(pubkey));
				Assert.Equal(Money.Coins(0.6m) + Money.Coins(0.15m), utxo.Unconfirmed.UTXOs[0].Value.Add(utxo.Unconfirmed.UTXOs[1].Value));
				Assert.Empty(utxo.Unconfirmed.SpentOutpoints);
			}
		}

		[FactWithTimeout]
		public async Task CanReserveAddress()
		{
			using (var tester = ServerTester.Create())
			{
				//WaitServerStarted not needed, just a sanity check
				var bob = tester.CreateDerivationStrategy();
				tester.Client.WaitServerStarted();
				await tester.Client.TrackAsync(bob);

				var tasks = new List<Task<KeyPathInformation>>();
				for (int i = 0; i < 100; i++)
				{
					tasks.Add(tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit, reserve: true));
				}
				await Task.WhenAll(tasks.ToArray());

				var paths = tasks.Select(t => t.Result).ToDictionary(c => c.KeyPath);
				Assert.Equal(99, paths.Select(p => p.Value.GetIndex(KeyPathTemplates.Default)).Max());

				tester.Client.CancelReservation(bob, new[] { new KeyPath("0/0") });
				var addr = await tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit);
				Assert.Equal(new KeyPath("0/0"), addr.KeyPath);

				var t = tester.SendToAddress(addr.Address, Money.Coins(0.6m));
				tester.Notifications.WaitForTransaction(bob, t);

				var addr2 = await tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit);
				Assert.Equal(new KeyPath("0/100"), addr2.KeyPath);
				// Cancellation on a used address shouldn't be possible
				tester.Client.CancelReservation(bob, new[] { new KeyPath("0/0") });
				addr2 = await tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit);
				Assert.Equal(new KeyPath("0/100"), addr2.KeyPath);
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

			Assert.Equal(factory.Parse($"2-of-{toto}-{tata}-[keeporder]-[legacy]").ToString(), factory.Parse($"2-of-{toto}-{tata}-[legacy]-[keeporder]").ToString());
			Assert.Equal(factory.Parse($"2-of-{toto}-{tata}-[p2sh]-[keeporder]").ToString(), factory.Parse($"2-of-{toto}-{tata}-[keeporder]-[p2sh]").ToString());

			factory.AuthorizedOptions.Add("a");
			factory.AuthorizedOptions.Add("b");
			Assert.Equal(factory.Parse($"2-of-{toto}-{tata}-[b]-[keeporder]-[a]-[legacy]").ToString(), factory.Parse($"2-of-{toto}-{tata}-[legacy]-[keeporder]-[a]-[b]").ToString());
			Assert.Equal(factory.Parse($"2-of-{toto}-{tata}-[a]-[p2sh]-[keeporder]-[b]").ToString(), factory.Parse($"2-of-{toto}-{tata}-[keeporder]-[p2sh]-[a]-[b]").ToString());

			var taproot = factory.Parse($"{toto}-[taproot]");
			Assert.Equal($"{toto}-[taproot]", taproot.ToString());
			generated = Generate(taproot);
			Assert.IsType<TaprootPubKey>(generated.ScriptPubKey.GetDestination());
		}

		private static Derivation Generate(DerivationStrategyBase strategy)
		{
			var derivation = strategy.GetLineFor(KeyPathTemplates.Default.GetKeyPathTemplate(DerivationFeature.Deposit)).Derive(1U);
			var derivation2 = strategy.GetDerivation(KeyPathTemplates.Default.GetKeyPathTemplate(DerivationFeature.Deposit).GetKeyPath(1U));
			Assert.Equal(derivation.Redeem, derivation2.Redeem);
			return derivation;
		}

		[FactWithTimeout]
		public async Task CanGetStatus()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted(Timeout);
				var status = await tester.Client.GetStatusAsync();
				Assert.NotNull(status.BitcoinStatus);
				Assert.Equal("CanGetStatus", status.InstanceName);
				Assert.True(status.IsFullySynched);
				Assert.Equal(status.BitcoinStatus.Blocks, status.BitcoinStatus.Headers);
				Assert.Equal(status.BitcoinStatus.Blocks, status.ChainHeight);
				Assert.Equal(1.0, status.BitcoinStatus.VerificationProgress);
				Assert.NotNull(status.Version);
				Assert.Equal(tester.CryptoCode, status.CryptoCode);
				Assert.Equal(ChainName.Regtest, status.NetworkType);
				Assert.Equal(tester.CryptoCode, status.SupportedCryptoCodes[0]);
				Assert.Single(status.SupportedCryptoCodes);
				Assert.NotNull(status.BitcoinStatus.Capabilities);
				var resp = await tester.HttpClient.GetAsync("/");
				Assert.Equal("CanGetStatus", resp.Headers.GetValues("instance-name").First());
			}
		}

		public CancellationToken Timeout => new CancellationTokenSource(10000).Token;


		[FactWithTimeout]
		public async Task CanGetTransactionsOfDerivation()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				await tester.Client.TrackAsync(pubkey);

				Logs.Tester.LogInformation("Let's send 1.0BTC to 0/0");
				var txId = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(pubkey, txId);
				Logs.Tester.LogInformation("Check if the tx exists");
				var result = await tester.Client.GetTransactionsAsync(pubkey);
				Assert.Single(result.UnconfirmedTransactions.Transactions);

				var height = result.Height;
				var timestampUnconf = result.UnconfirmedTransactions.Transactions[0].Timestamp;
				Assert.Null(result.UnconfirmedTransactions.Transactions[0].BlockHash);
				Assert.Null(result.UnconfirmedTransactions.Transactions[0].Height);
				Assert.Equal(0, result.UnconfirmedTransactions.Transactions[0].Confirmations);
				Assert.Equal(result.UnconfirmedTransactions.Transactions[0].Transaction.GetHash(), result.UnconfirmedTransactions.Transactions[0].TransactionId);
				Assert.Equal(Money.Coins(1.0m), result.UnconfirmedTransactions.Transactions[0].BalanceChange);

				Logs.Tester.LogInformation("Sanity check that if we filter the transaction, we get only the expected one");
				var tx1 = await tester.Client.GetTransactionAsync(pubkey, txId);
				Assert.NotNull(tx1);
				Assert.Equal(Money.Coins(1.0m), tx1.BalanceChange);
				Assert.Null(tester.Client.GetTransaction(pubkey, uint256.One));

				tester.Client.IncludeTransaction = false;
				result = await tester.Client.GetTransactionsAsync(pubkey);
				Assert.Null(result.UnconfirmedTransactions.Transactions[0].Transaction);

				Logs.Tester.LogInformation("Let's mine and send 1.0BTC to 0");
				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForTransaction(pubkey, txId);
				result = await tester.Client.GetTransactionsAsync(pubkey);
				var txId2 = tester.SendToAddress(tester.AddressOf(key, "0"), Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(pubkey, txId2);
				Logs.Tester.LogInformation("We should now have two transactions");
				result = await tester.Client.GetTransactionsAsync(pubkey);
				Assert.Single(result.ConfirmedTransactions.Transactions);
				Assert.Single(result.UnconfirmedTransactions.Transactions);
				Assert.Equal(txId2, result.UnconfirmedTransactions.Transactions[0].TransactionId);
				Assert.Equal(txId, result.ConfirmedTransactions.Transactions[0].TransactionId);
				Assert.Equal(timestampUnconf, result.ConfirmedTransactions.Transactions[0].Timestamp);

				Logs.Tester.LogInformation("Let's send from 0/0 to 0/1");
				LockTestCoins(tester.RPC);
				tester.ImportPrivKey(key, "0/0");
				var txId3 = tester.SendToAddress(tester.AddressOf(key, "0/1"), Money.Coins(0.2m));
				tester.Notifications.WaitForTransaction(pubkey, txId3);
				result = await tester.Client.GetTransactionsAsync(pubkey);
				Assert.Equal(2, result.UnconfirmedTransactions.Transactions.Count);
				Assert.Equal(Money.Coins(-0.8m), result.UnconfirmedTransactions.Transactions[0].BalanceChange);
				var tx3 = await tester.Client.GetTransactionAsync(pubkey, txId3);
				Assert.Equal(Money.Coins(-0.8m), tx3.BalanceChange);
			}
		}

		[FactWithTimeout]
		public async Task CanTrack5()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				await tester.Client.TrackAsync(pubkey);

				Logs.Tester.LogInformation("Send 1.0BTC to 0/0");
				var fundingTx = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				var utxo = await tester.Client.GetUTXOsAsync(pubkey);
				tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Single(utxo.Confirmed.UTXOs);

				Logs.Tester.LogInformation("Send 0.2BTC from the 0/0 to a random address");
				LockTestCoins(tester.RPC);
				tester.ImportPrivKey(key, "0/0");
				var spendingTx = tester.SendToAddress(new Key().PubKey.Hash.GetAddress(tester.Network), Money.Coins(0.2m));
				tester.Notifications.WaitForTransaction(pubkey, spendingTx);
				Logs.Tester.LogInformation("Check we have empty UTXO as unconfirmed");
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Single(utxo.Unconfirmed.SpentOutpoints);
				Assert.Equal(fundingTx, utxo.Unconfirmed.SpentOutpoints[0].Hash);
				tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));

				Logs.Tester.LogInformation("Let's check if direct addresses can be tracked by sending to 0");
				var address = await tester.Client.GetUnusedAsync(pubkey, DerivationFeature.Direct);
				Assert.Equal(DerivationFeature.Direct, address.Feature);
				fundingTx = tester.SendToAddress(tester.AddressOf(key, "0"), Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(pubkey, fundingTx);
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Equal(address.ScriptPubKey, utxo.Unconfirmed.UTXOs[0].ScriptPubKey);
				var address2 = await tester.Client.GetUnusedAsync(pubkey, DerivationFeature.Direct);
				Assert.Equal(new KeyPath(1), address2.KeyPath);

				Logs.Tester.LogInformation("Let's check see if an unconf tx can be conf then unconf again");
				tester.RPC.Generate(1);
				tester.Notifications.WaitForTransaction(pubkey, fundingTx);
				var expectedUTXO = utxo.Unconfirmed.UTXOs[0].Outpoint;
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Contains(expectedUTXO, utxo.Confirmed.UTXOs.Select(o => o.Outpoint));
				Assert.DoesNotContain(expectedUTXO, utxo.Unconfirmed.UTXOs.Select(o => o.Outpoint));
				await tester.RPC.InvalidateBlockAsync(tester.RPC.GetBestBlockHash());
				await tester.RPC.SendCommandAsync("generateblock", new object[] {
					new Key().GetAddress(ScriptPubKeyType.Legacy, tester.Network).ToString(),
					new JArray()
				});
				tester.Notifications.WaitForBlocks(tester.RPC.GetBestBlockHash());
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.DoesNotContain(expectedUTXO, utxo.Confirmed.UTXOs.Select(o => o.Outpoint));
				Assert.Contains(expectedUTXO, utxo.Unconfirmed.UTXOs.Select(o => o.Outpoint));
			}
		}

		[FactWithTimeout]
		public async Task CanRescan()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted(Timeout);
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());

				var txId1 = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				var txId2 = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				var txId3 = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				var txId4 = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				var tx4 = tester.RPC.GetRawTransaction(txId4);
				var notify = tester.Client.CreateWebsocketNotificationSession();
				notify.ListenNewBlock();
				var blockId = tester.RPC.Generate(1)[0];
				var blockId2 = tester.RPC.Generate(1)[0];

				notify.NextEvent();
				await tester.Client.TrackAsync(pubkey);

				var utxos = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Empty(utxos.Confirmed.UTXOs);

				for (int i = 0; i < 2; i++)
				{
					tester.Client.Rescan(new RescanRequest()
					{
						Transactions =
						{
							new RescanRequest.TransactionToRescan() { BlockId = blockId, TransactionId = txId1 },
							new RescanRequest.TransactionToRescan() { BlockId = blockId2, TransactionId = txId2 }, // should fail because wrong block
							new RescanRequest.TransactionToRescan() {  TransactionId = txId3 },  // should work because -txindex
							new RescanRequest.TransactionToRescan() { BlockId = blockId, Transaction = tx4 },  // should find it
						}
					});

					utxos = await tester.Client.GetUTXOsAsync(pubkey);
					foreach (var txid in new[] { txId1, txId4, txId3 })
					{
						Assert.Contains(utxos.Confirmed.UTXOs, u => u.AsCoin().Outpoint.Hash == txid);
						var tx = tester.Client.GetTransaction(txid);
						Assert.Equal(2, tx.Confirmations);
					}
					Assert.Equal(3, tester.Client.GetTransactions(pubkey).ConfirmedTransactions.Transactions.Count);
					foreach (var utxo in utxos.Confirmed.UTXOs)
						Assert.Equal(2, utxo.Confirmations);
					foreach (var txid in new[] { txId2 })
					{
						Assert.DoesNotContain(utxos.Confirmed.UTXOs, u => u.AsCoin().Outpoint.Hash == txid);
					}
				}
			}
		}

		[FactWithTimeout]
		public async Task CanTrackManyAddressesAtOnce()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());

				await tester.Client.TrackAsync(pubkey, new TrackWalletRequest()
				{
					Wait = true,
					DerivationOptions = new TrackDerivationOption[]
					{
						new TrackDerivationOption()
						{
							Feature = DerivationFeature.Deposit,
							MinAddresses = 500
						}
					}
				});

#pragma warning disable CS0618 // Type or member is obsolete
				var info = await tester.Client.GetKeyInformationsAsync(pubkey.GetDerivation(new KeyPath("0/499")).ScriptPubKey);
				Assert.Single(info);
#pragma warning restore CS0618 // Type or member is obsolete
			}
		}

		[FactWithTimeout]
		public async Task CanTrack()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());

				await tester.Client.TrackAsync(pubkey);
				Logs.Tester.LogInformation("Sending 1.0 BTC to 0/0");
				var txId = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(pubkey, txId);
				Logs.Tester.LogInformation("Making sure the BTC is properly received");
				var utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Equal(tester.Network.Consensus.CoinbaseMaturity + 1, utxo.CurrentHeight);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				
				Assert.Equal(tester.AddressOf(key, "0/0"), utxo.Unconfirmed.UTXOs[0].Address);
				Assert.Equal(txId, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				var unconfTimestamp = utxo.Unconfirmed.UTXOs[0].Timestamp;
				Assert.Equal(0, utxo.Unconfirmed.UTXOs[0].Confirmations);
				Assert.Empty(utxo.Confirmed.UTXOs);

				Logs.Tester.LogInformation("Making sure we can query the transaction");
				var tx = await tester.Client.GetTransactionAsync(utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				Assert.NotNull(tx);
				Assert.Equal(0, tx.Confirmations);
				Assert.Null(tx.BlockId);
				Assert.Equal(utxo.Unconfirmed.UTXOs[0].Outpoint.Hash, tx.Transaction.GetHash());
				Assert.Equal(unconfTimestamp, tx.Timestamp);

				Logs.Tester.LogInformation("Let's mine and wait for notification");
				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForTransaction(pubkey, txId);
				Logs.Tester.LogInformation("Let's see if our UTXO is properly confirmed");
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Empty(utxo.Unconfirmed.UTXOs);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(txId, utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.Equal(1, utxo.Confirmed.UTXOs[0].Confirmations);
				Assert.Equal(unconfTimestamp, utxo.Confirmed.UTXOs[0].Timestamp);

				Logs.Tester.LogInformation("Let's send 1.0 BTC to 0/1");
				var confTxId = txId;
				txId = await tester.SendToAddressAsync(tester.AddressOf(key, "0/1"), Money.Coins(1.0m));
				var txId01 = txId;
				tester.Notifications.WaitForTransaction(pubkey, txId);

				Logs.Tester.LogInformation("Let's see if we have both: an unconf UTXO and a conf one");
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(new KeyPath("0/1"), utxo.Unconfirmed.UTXOs[0].KeyPath);
				Assert.Equal(txId, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(new KeyPath("0/0"), utxo.Confirmed.UTXOs[0].KeyPath);
				Assert.Equal(confTxId, utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.Equal(1, utxo.Confirmed.UTXOs[0].Confirmations);

				Logs.Tester.LogInformation("Let's check what happen if querying a non existing transaction");
				Assert.Null(tester.Client.GetTransaction(uint256.One));
				Logs.Tester.LogInformation("Let's check what happen if querying the confirmed transaction");
				tx = await tester.Client.GetTransactionAsync(utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.NotNull(tx);
				Assert.Equal(unconfTimestamp, tx.Timestamp);
				Assert.Equal(1, tx.Confirmations);
				Assert.NotNull(tx.BlockId);
				Assert.Equal(utxo.Confirmed.UTXOs[0].Outpoint.Hash, tx.Transaction.GetHash());

				Logs.Tester.LogInformation("Let's mine, we should not have 2 confirmed UTXO");
				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForTransaction(pubkey, txId);
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Equal(2, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/0"), utxo.Confirmed.UTXOs[0].KeyPath);
				Assert.Equal(new KeyPath("0/1"), utxo.Confirmed.UTXOs[1].KeyPath);

				Logs.Tester.LogInformation("Let's check that we can query the UTXO with 2 confirmations");
				tx = tester.Client.GetTransaction(tx.Transaction.GetHash());
				Assert.Equal(2, tx.Confirmations);
				Assert.NotNull(tx.BlockId);

				var outpoint01 = utxo.Confirmed.UTXOs[0].Outpoint;

				Logs.Tester.LogInformation("Let's send 1.0BTC to 0/2 and mine");
				txId = tester.SendToAddress(tester.AddressOf(key, "0/2"), Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(pubkey, txId);
				var txId1 = txId;
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(2, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/2"), utxo.Unconfirmed.UTXOs[0].KeyPath);
				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForTransaction(pubkey, txId);

				Logs.Tester.LogInformation("We should have 3 UTXO (0/0, 0/1, 0/2)");
				utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Equal(3, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/0"), utxo.Confirmed.UTXOs[0].KeyPath);
				Assert.Equal(new KeyPath("0/1"), utxo.Confirmed.UTXOs[1].KeyPath);
				Assert.Equal(new KeyPath("0/2"), utxo.Confirmed.UTXOs[2].KeyPath);

				tx = tester.Client.GetTransaction(tx.Transaction.GetHash());
				Assert.Equal(3, tx.Confirmations);
				Assert.NotNull(tx.BlockId);

				Logs.Tester.LogInformation("Let's send 0.5 BTC from 0/1 to 0/3");
				LockTestCoins(tester.RPC);
				await tester.ImportPrivKeyAsync(key, "0/1");
				txId = await tester.SendToAddressAsync(tester.AddressOf(key, "0/3"), Money.Coins(0.5m));
				tester.Notifications.WaitForTransaction(pubkey, txId);

				Logs.Tester.LogInformation("We should have one unconf UTXO, and one spent from the confirmed UTXOs");
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(new KeyPath("0/3"), utxo.Unconfirmed.UTXOs[0].KeyPath);
				Assert.Equal(txId01, utxo.Unconfirmed.SpentOutpoints[0].Hash);

				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForTransaction(pubkey, txId);

				Logs.Tester.LogInformation("After mining, we should have only 3 UTXO from 0/0, 0/2 and 0/3 (change did not go back to the wallet)");
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Equal(3, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/0"), utxo.Confirmed.UTXOs[0].KeyPath);
				Assert.Equal(new KeyPath("0/2"), utxo.Confirmed.UTXOs[1].KeyPath);
				Assert.Equal(new KeyPath("0/3"), utxo.Confirmed.UTXOs[2].KeyPath);

				Logs.Tester.LogInformation("Making sure we can query a transaction our wallet does not know about if txindex=1");
				txId = tester.SendToAddress(new Key().GetScriptPubKey(ScriptPubKeyType.Legacy), Money.Coins(1.0m));
				Assert.NotNull(tester.Client.GetTransaction(txId));
				var blockId = tester.Explorer.Generate(1);
				tester.Notifications.WaitForBlocks(blockId);
				var savedTx = tester.Client.GetTransaction(txId);
				Assert.Equal(blockId[0], savedTx.BlockId);

				// Ensure the current state is correct
				var balance = tester.Client.GetBalance(pubkey);
				Assert.Equal(Money.Coins(2.5m), balance.Confirmed);
				Assert.Equal(Money.Coins(0.0m), balance.Unconfirmed);
				Assert.Equal(Money.Coins(2.5m), balance.Total);
				Assert.Equal(Money.Coins(0.0m), balance.Immature);
				Assert.Equal(Money.Coins(2.5m), balance.Available);
				Logs.Tester.LogInformation("Let's mine, and check that the balance is not updated until maturity occurs");
				var blkid = tester.RPC.GenerateToAddress(1, tester.AddressOf(key, "0/0"))[0];
				var blk = tester.RPC.GetBlock(blkid);
				var minedTxId = blk.Transactions[0].GetHash();
				tester.Notifications.WaitForTransaction(pubkey, minedTxId);
				balance = tester.Client.GetBalance(pubkey);
				Assert.Equal(Money.Coins(52.5m), balance.Confirmed);
				Assert.Equal(Money.Coins(0.0m), balance.Unconfirmed);
				Assert.Equal(Money.Coins(52.5m), balance.Total);
				Assert.Equal(Money.Coins(50.0m), balance.Immature);
				Assert.Equal(Money.Coins(2.5m), balance.Available);
				var utxos = tester.Client.GetUTXOs(pubkey);
				Assert.DoesNotContain(utxos.Confirmed.UTXOs, u => u.Outpoint.Hash == minedTxId);
				Assert.DoesNotContain(utxos.Unconfirmed.UTXOs, u => u.Outpoint.Hash == minedTxId);
				var transactions = tester.Client.GetTransactions(pubkey);
				Assert.Contains(transactions.ConfirmedTransactions.Transactions, u => u.TransactionId == minedTxId);
				Assert.Contains(transactions.ImmatureTransactions.Transactions, u => u.TransactionId == minedTxId);
				// Let's generate enough block and see if the transaction is finally mature
				var blockIds = tester.RPC.Generate(tester.Network.Consensus.CoinbaseMaturity);
				tester.Notifications.WaitForBlocks(blockIds);
				balance = tester.Client.GetBalance(pubkey);
				Assert.Equal(Money.Coins(52.5m), balance.Confirmed);
				Assert.Equal(Money.Coins(0.0m), balance.Unconfirmed);
				Assert.Equal(Money.Coins(52.5m), balance.Total);
				Assert.Equal(Money.Coins(0.0m), balance.Immature);
				Assert.Equal(Money.Coins(52.5m), balance.Available);
				transactions = tester.Client.GetTransactions(pubkey);
				utxos = tester.Client.GetUTXOs(pubkey);
				Assert.Empty(transactions.ImmatureTransactions.Transactions);
				Assert.Contains(utxos.Confirmed.UTXOs, u => u.Outpoint.Hash == minedTxId);
				Assert.Contains(transactions.ConfirmedTransactions.Transactions, u => u.TransactionId == minedTxId);
			}
		}
		[FactWithTimeout]
		public async Task CanCacheTransactions()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				Logs.Tester.LogInformation("Let's check an unconf miss result get properly cached: Let's send coins to 0/1 before tracking it");
				await tester.RPC.GenerateAsync(1);
				var txId = await tester.SendToAddressAsync(tester.AddressOf(key, "0/1"), Money.Coins(1.0m));
				Logs.Tester.LogInformation($"Sent {txId}");
				await Task.Delay(1000);
				await tester.Client.TrackAsync(pubkey);
				Logs.Tester.LogInformation($"Tracked, let's mine");
				tester.Notifications.WaitForBlocks(tester.RPC.Generate(1));
				var utxo = await tester.Client.GetUTXOsAsync(pubkey);
				Assert.Empty(utxo.Confirmed.UTXOs);
				Assert.Empty(utxo.Unconfirmed.UTXOs);
			}
		}
		[Fact(Timeout = 60 * 1000)]
		public async Task CanUseLongPollingOnEvents()
		{
			using (var tester = ServerTester.Create())
			{
				//WaitServerStarted not needed, just a sanity check
				tester.Client.WaitServerStarted(Timeout);
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);

				Logs.Tester.LogInformation("Get events should not returns with long polling as not event happen");
				var session = tester.Client.CreateLongPollingNotificationSession();
				var evts = session.GetEvents();
				long lastId = 0;
				if (evts.Length != 0)
					lastId = evts.Last().EventId;
				DateTimeOffset now = DateTimeOffset.UtcNow;
				using (var cts = new CancellationTokenSource(1500))
				{
					try
					{
						evts = session.GetEvents(lastId, longPolling: true, cancellation: cts.Token);
						lastId = evts.Last().EventId;
						evts = session.GetEvents(lastId, longPolling: true, cancellation: cts.Token);
						Assert.Fail("Should throws");
					}
					catch (OperationCanceledException)
					{

					}
					Assert.True(cts.IsCancellationRequested);
				}
				long lastId2 = 0;
				if (evts.Length != 0)
					lastId2 = evts.Last().EventId;
				Assert.Equal(lastId, lastId2);
				Assert.True(DateTimeOffset.UtcNow - now > TimeSpan.FromSeconds(1.0));
				Logs.Tester.LogInformation("Get events should returns when the block get mined");
				now = DateTimeOffset.UtcNow;
				var gettingEvts = session.GetEventsAsync(lastEventId: lastId, longPolling: true);
				Thread.Sleep(1000);
				Assert.False(gettingEvts.IsCompleted);
				tester.RPC.Generate(1);
				Logs.Tester.LogInformation("Block mined");
				evts = await gettingEvts;
				Assert.Equal(lastId + 1, evts.Last().EventId);
				Assert.Single(evts);
				Assert.True(DateTimeOffset.UtcNow - now < TimeSpan.FromSeconds(5.0));
				Logs.Tester.LogInformation("Event returned");
				Logs.Tester.LogInformation("Should return immediately because the wallet received money");
				now = DateTimeOffset.UtcNow;
				tester.RPC.SendToAddress(tester.Client.GetUnused(pubkey, DerivationFeature.Deposit).ScriptPubKey, Money.Coins(1.0m));
				evts = session.GetEvents(lastEventId: evts.Last().EventId, longPolling: true);
				Assert.True(DateTimeOffset.UtcNow - now < TimeSpan.FromSeconds(5.0));
				Logs.Tester.LogInformation("Event returned");
				evts = session.GetEvents();
				// With the postgres backend, when the indexer starts first time, it asks
				// for the blocks previous to the highest block. So we get one more event.
				// This is harmless.
				evts = evts.Skip(1).ToArray();
				Assert.Equal(2, evts.Length);
				Assert.IsType<Models.NewBlockEvent>(evts[0]);
				Assert.IsType<Models.NewTransactionEvent>(evts[1]);
			}
		}
		private void LockTestCoins(RPCClient rpc, HashSet<Script> keepAddresses = null)
		{
			if (keepAddresses == null)
			{
				var outpoints = rpc.ListUnspent().Select(o => o.OutPoint).ToArray();
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
		public void CanTopologicalSortRecords()
		{
			var key = new BitcoinExtKey(new ExtKey(), Network.RegTest);
			var pubkey = GetNetwork(Bitcoin.Instance).DerivationStrategyFactory.Parse($"{key.Neuter().ToString()}");
			var trackedSource = new DerivationSchemeTrackedSource(pubkey);

			// The topological sorting should always return the most buried transactions first
			// So if a transaction is deemed younger, it should be returned after
			var tx1 = CreateRandomAnnotatedTransaction(trackedSource, 1);
			var tx2 = CreateRandomAnnotatedTransaction(trackedSource, 2);
			AssertExpectedOrder(new[] { tx1, tx2 }); // tx2 has higher height (younger) so after tx1

			tx1 = CreateRandomAnnotatedTransaction(trackedSource, null);
			tx2 = CreateRandomAnnotatedTransaction(trackedSource, 2);
			AssertExpectedOrder(new[] { tx2, tx1 }); // tx1 is not confirmed should appear after

			tx1 = CreateRandomAnnotatedTransaction(trackedSource, seen: 1);
			tx2 = CreateRandomAnnotatedTransaction(trackedSource, seen: 2);
			AssertExpectedOrder(new[] { tx1, tx2 }); // tx1 has been seen before tx2, thus should be returned first

			tx1 = CreateRandomAnnotatedTransaction(trackedSource, seen: 1);
			tx2 = CreateRandomAnnotatedTransaction(trackedSource, seen: 2);

			var outpoint = new OutPoint(tx2.Record.Key.TxId, 0);
			tx1.Record.SpentOutpoints.Add(outpoint, 0);
			tx2.Record.ReceivedCoins.Add(new Coin(outpoint, new TxOut()));
			AssertExpectedOrder(new[] { tx2, tx1 }, true); // tx1 depends on tx2 so even if tx1 has been seen first, topological sort should be used

			List<AnnotatedTransaction> txs = new List<AnnotatedTransaction>();
			Random r = new Random();
			for (int i = 0; i < 20_000; i++)
			{
				txs.Add(CreateRandomAnnotatedTransaction(trackedSource, r.Next(0, 5000)));
			}
			var sorted = txs.TopologicalSort();
			var highest = 0L;
			foreach (var tx in sorted)
			{
				if (tx.Height.Value < highest)
					Assert.Fail("Transactions out of order");
				highest = Math.Max(highest, tx.Height.Value);
			}
		}

		private void AssertExpectedOrder(AnnotatedTransaction[] annotatedTransaction, bool skipDictionaryTest = false)
		{
			var input = annotatedTransaction.ToArray();
			for (int u = 0; u < 4; u++)
			{
				NBitcoin.Utils.Shuffle(input);
				var result = input.TopologicalSort().ToArray();
				var dico = new SortedDictionary<AnnotatedTransaction, AnnotatedTransaction>(AnnotatedTransactionComparer.OldToYoung);
				foreach (var tx in annotatedTransaction)
				{
					dico.Add(tx, tx);
				}
				var result2 = dico.Select(o => o.Value).ToArray();
				Assert.Equal(annotatedTransaction.Length, result.Length);
				Assert.Equal(annotatedTransaction.Length, result2.Length);
				for (int i = 0; i < annotatedTransaction.Length; i++)
				{
					Assert.Equal(annotatedTransaction[i], result[i]);
					if (!skipDictionaryTest)
						Assert.Equal(annotatedTransaction[i], result2[i]);
				}
			}
		}

		private static AnnotatedTransaction CreateRandomAnnotatedTransaction(DerivationSchemeTrackedSource trackedSource, int? height = null, int? seen = null)
		{
			var a = new AnnotatedTransaction(height, new TrackedTransaction(new TrackedTransactionKey(RandomUtils.GetUInt256(), null, true), trackedSource, null as Coin[], null), true);
			if (seen is int v)
			{
				a.Record.FirstSeen = NBitcoin.Utils.UnixTimeToDateTime(v);
			}
			return a;
		}

		[Fact]
		public void CanTopologicalSortTx()
		{
			var tx1 = Transaction.Create(Network.Main);
			tx1.Outputs.Add(Money.Zero, new Key());
			var tx2 = Transaction.Create(Network.Main);
			tx2.Inputs.Add(new OutPoint(tx1, 0));
			var tx3 = Transaction.Create(Network.Main);
			tx3.Inputs.Add(new OutPoint(tx2, 0));
			tx3.Outputs.Add(Money.Zero, new Key());
			var arr = new[] { tx2, tx1, tx3 };
			var expected = new[] { tx1, tx2, tx3 };
			var actual = arr.TopologicalSort(o => o.Inputs.Select(i => i.PrevOut.Hash), o => o.GetHash()).ToArray();
			Assert.True(expected.SequenceEqual(actual));
		}

		/// <summary>
		/// To understand this test, read https://github.com/dgarage/NBXplorer/blob/master/docs/Design.md
		/// This create a specific graph of transaction and make sure that it computes the UTXO set as expected.
		/// </summary>
		[Fact]
		public void CanCalculateCorrectUTXOSet()
		{
			uint256 ToUint256(byte o)
			{
				var bytes = new byte[32];
				bytes[0] = o;
				return new uint256(bytes);
			}
			var chain = new SlimChain(uint256.Zero);
			var blocks = Enumerable.Range(1, 3).Select(ii => ToUint256((byte)ii)).ToArray();
			for (int idx = 0; idx < blocks.Length; idx++)
			{
				chain.TrySetTip(blocks[idx], idx == 0 ? uint256.Zero : blocks[idx - 1]);
			}
			TrackedTransactionBuilder builder = new TrackedTransactionBuilder();
			builder.CreateTransaction(out var _73bdee)
				.AddOutput(out var a)
				.AddOutput(out var b)
				.MinedBy(blocks[0]);

			builder.CreateTransaction(out var ab3def)
				.AddOutput(out var c)
				.AddOutput(out var d)
				.MinedBy(blocks[0]);

			builder.CreateTransaction(out var _452bdd)
				.AddOutput(out var e)
				.AddOutput(out var f)
				.AddOutput(out var g)
				.Spend(c)
				.Spend(a)
				.MinedBy(blocks[1]);

			builder.CreateTransaction(out var ef7dfa)
				.AddOutput(out var h)
				.Spend(e)
				.MinedBy(blocks[2]);

			builder.CreateTransaction(out var dd483a)
				.Spend(h)
				.Spend(g)
				.MinedBy(blocks[2]);

			builder.CreateTransaction(out var _2bdac2)
				.AddOutput(out var i)
				.AddOutput(out var j)
				.Spend(b);

			builder.CreateTransaction(out var _17b3b3)
				.Spend(i)
				.Timestamp(1);

			builder.CreateTransaction(out var ab3922)
				.AddOutput(out var k)
				.Spend(i)
				.Timestamp(2);

			builder.Dup(_17b3b3, out var _17b3b3dup)
				   .Timestamp(0);

			builder.Dup(ab3922, out var ab3922dup)
				   .Timestamp(0);

			var trackedTransactions = builder.Build();
			AddBlockHeight(chain, trackedTransactions);

			bool IsEqual(AnnotatedTransaction tx, TrackedTransactionBuilder.TransactionContext ctx)
			{
				return tx.Record.TransactionHash == ctx._TransactionId && tx.Record.Inserted == ctx._TimeStamp;
			}

			for (int iii = 0; iii < 100; iii++)
			{
				NBitcoin.Utils.Shuffle(trackedTransactions);
				var collection = new AnnotatedTransactionCollection(trackedTransactions, builder._TrackedSource, Network.RegTest);
				Assert.Equal(7, collection.Count);

				Assert.Single(collection.ReplacedTransactions);
				Assert.Contains(collection.ReplacedTransactions, r => IsEqual(r, _17b3b3));

				Assert.Equal(2, collection.CleanupTransactions.Count);
				foreach (var cleaned in new[] { _17b3b3dup, ab3922dup })
				{
					Assert.Contains(collection.CleanupTransactions, r => IsEqual(r, cleaned));
				}

				Assert.Equal(2, collection.UnconfirmedTransactions.Count);
				foreach (var unconf in new[] { ab3922, _2bdac2 })
				{
					Assert.Contains(collection.UnconfirmedTransactions, r => IsEqual(r, unconf));
				}

				Assert.Equal(7, collection.UnconfirmedState.SpentUTXOs.Count);
				foreach (var spent in new[] { a, b, c, e, g, h, i })
				{
					Assert.Contains(spent.Coin.Outpoint, collection.UnconfirmedState.SpentUTXOs);
					Assert.False(collection.UnconfirmedState.UTXOByOutpoint.ContainsKey(spent.Coin.Outpoint));
				}
				Assert.Equal(4, collection.UnconfirmedState.UTXOByOutpoint.Count());
				foreach (var unspent in new[] { d, f, j, k })
				{
					Assert.DoesNotContain(unspent.Coin.Outpoint, collection.UnconfirmedState.SpentUTXOs);
					Assert.True(collection.UnconfirmedState.UTXOByOutpoint.ContainsKey(unspent.Coin.Outpoint));
				}

				foreach (var t in new[] { _73bdee, ab3922, _452bdd, ef7dfa, dd483a, _2bdac2, ab3922 })
				{
					Assert.NotNull(collection.GetByTxId(t._TransactionId));
				}

				Assert.Null(collection.GetByTxId(_17b3b3._TransactionId));

				var tx = collection.GetByTxId(ab3922dup._TransactionId);
				Assert.Equal(ab3922._TimeStamp, tx.Record.Inserted);
				Assert.NotEqual(ab3922dup._TimeStamp, tx.Record.Inserted);
			}

			var lastBlock = ToUint256(10);
			chain.TrySetTip(lastBlock, blocks.Last());

			ab3922.MinedBy(lastBlock);
			_2bdac2.MinedBy(lastBlock);
			trackedTransactions = builder.Build();
			AddBlockHeight(chain, trackedTransactions);
			for (int iii = 0; iii < 100; iii++)
			{
				NBitcoin.Utils.Shuffle(trackedTransactions);
				var collection = new AnnotatedTransactionCollection(trackedTransactions, builder._TrackedSource, Network.RegTest);
				Assert.Empty(collection.ReplacedTransactions);
				Assert.Empty(collection.UnconfirmedTransactions);
				Assert.Equal(3, collection.CleanupTransactions.Count);
				foreach (var dup in new[] { _17b3b3, _17b3b3dup, ab3922dup })
				{
					Assert.Contains(collection.CleanupTransactions, t => IsEqual(t, dup));
				}
			}
		}

		private static void AddBlockHeight(SlimChain chain, TrackedTransaction[] trackedTransactions)
		{
			foreach (var trackedTx in trackedTransactions)
			{
				if (trackedTx.BlockHash != null && chain.TryGetHeight(trackedTx.BlockHash, out var height))
				{
					trackedTx.BlockHeight = height;
				}
			}
		}

		[FactWithTimeout]
		public async Task CanBroadcast()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var tx = tester.Network.Consensus.ConsensusFactory.CreateTransaction();
				tx.Outputs.Add(Money.Coins(1.0m), new Key());
				var funded = await tester.RPC.FundRawTransactionAsync(tx);
				var signed = await tester.RPC.SignRawTransactionAsync(funded.Transaction);
				var result = await tester.Client.BroadcastAsync(signed);
				Assert.True(result.Success);
				signed.Inputs[0].PrevOut.N = 999;
				result = await tester.Client.BroadcastAsync(signed);
				Assert.False(result.Success);
				var ex = await Assert.ThrowsAsync<NBXplorerException>(() => tester.Client.GetFeeRateAsync(5));
				Assert.Equal("fee-estimation-unavailable", ex.Error.Code);
			}
		}

		[FactWithTimeout]
		public async Task CanGetKeyInformations()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				await tester.Client.TrackAsync(pubkey);

				KeyPathInformation[] keyinfos;
				var script = pubkey.GetDerivation(new KeyPath("0/0")).ScriptPubKey;

#pragma warning disable CS0618 // Type or member is obsolete
				keyinfos = await tester.Client.GetKeyInformationsAsync(script);
#pragma warning restore CS0618 // Type or member is obsolete
				Assert.NotNull(keyinfos);
				Assert.True(keyinfos.Length > 0);
				foreach (var k in keyinfos)
				{
					Assert.Equal(pubkey, k.DerivationStrategy);
					Assert.Equal(script, k.ScriptPubKey);
					Assert.Equal(new KeyPath("0/0"), k.KeyPath);
					Assert.Equal(DerivationFeature.Deposit, k.Feature);
				}

				var keyInfo = await tester.Client.GetKeyInformationAsync(pubkey, pubkey.GetDerivation(new KeyPath("0/0")).ScriptPubKey);
				Assert.NotNull(keyInfo?.Address);
				Assert.Null(await tester.Client.GetKeyInformationAsync(pubkey, pubkey.GetDerivation(new KeyPath("0/100")).ScriptPubKey));

				key = new BitcoinExtKey(new ExtKey(), tester.Network);
				pubkey = tester.CreateDerivationStrategy(key.Neuter());
				Assert.Null(await tester.Client.GetKeyInformationAsync(pubkey, pubkey.GetDerivation(new KeyPath("0/0")).ScriptPubKey));

				var pubkey2 = tester.NBXplorerNetwork.DerivationStrategyFactory.Parse("tpubD6NzVbkrYhZ4WxGZajmwTaAcWgUMTx8Syf2JwakRtGLjxTa8L8aZpYq4zas8yhr6XRCoSaQKjdmjMe8x8FuBFLe4HnEs3NSQWXAh7Pjnvoa-[p2sh]");
				tester.Client.Track(pubkey2);
				var ki = await tester.Client.GetUnusedAsync(pubkey2, DerivationFeature.Deposit);
				Assert.Equal("00149ef4739460cd69a19598a651a42ca91a9865b74f", ki.Redeem.ToHex());
				Assert.Equal("2MuawW29mtrQzJSVyHkaaoS1RjBE2oLYFjD", ki.Address.ToString());
			}
		}

		[Fact]
		public void CanCalculateScanningProgress()
		{
			ScanUTXOProgress progress = new ScanUTXOProgress();
			progress.RemainingBatches = 1;
			progress.BatchNumber = 0;
			progress.CurrentBatchProgress = 100;
			progress.UpdateOverallProgress();
			Assert.Equal(50, progress.OverallProgress);
			progress.CurrentBatchProgress = 50;
			progress.UpdateOverallProgress();
			Assert.Equal(25, progress.OverallProgress);
			progress.CurrentBatchProgress = 0;
			progress.UpdateOverallProgress();
			Assert.Equal(0, progress.OverallProgress);
			progress.BatchNumber = 1;
			progress.RemainingBatches = 0;
			progress.CurrentBatchProgress = 50;
			progress.UpdateOverallProgress();
			Assert.Equal(75, progress.OverallProgress);
			progress.RemainingBatches = 1;
			progress.CurrentBatchProgress = 100;
			progress.UpdateOverallProgress();
			Assert.Equal(67, (int)progress.OverallProgress);
			progress.RemainingBatches = 0;
			progress.BatchNumber = 2;
			progress.CurrentBatchProgress = 0;
			progress.UpdateOverallProgress();
			Assert.Equal(67, (int)progress.OverallProgress);

			DateTimeOffset time = new DateTimeOffset(0, TimeSpan.Zero);
			progress.StartedAt = time;
			progress.UpdateOverallProgress(time + TimeSpan.FromSeconds(10));
			Assert.Equal(3, progress.RemainingSeconds);

			progress = new ScanUTXOProgress();
			progress.From = 0;
			progress.Count = 100;
			progress.BatchNumber = 0;
			progress.HighestKeyIndexFound.AddOrReplace(DerivationFeature.Deposit, null);
			progress.UpdateRemainingBatches(1000);
			Assert.Equal(9, progress.RemainingBatches);
			progress.BatchNumber = 1;
			progress.UpdateRemainingBatches(1000);
			Assert.Equal(8, progress.RemainingBatches);
			progress.BatchNumber = 1 + 9;
			progress.UpdateRemainingBatches(1000);
			Assert.Equal(-1, progress.RemainingBatches);
			progress.BatchNumber = 0;
			progress.HighestKeyIndexFound.AddOrReplace(DerivationFeature.Deposit, 0);
			progress.UpdateRemainingBatches(1000);
			Assert.Equal(10, progress.RemainingBatches);
			progress.BatchNumber = 7;
			progress.UpdateRemainingBatches(1000);
			Assert.Equal(10 - 7, progress.RemainingBatches);
			progress.HighestKeyIndexFound.AddOrReplace(DerivationFeature.Deposit, 99);
			progress.UpdateRemainingBatches(1000);
			Assert.Equal(10 - 7, progress.RemainingBatches);
			progress.HighestKeyIndexFound.AddOrReplace(DerivationFeature.Deposit, 100);
			progress.UpdateRemainingBatches(1000);
			Assert.Equal(10 - 7 + 1, progress.RemainingBatches);
		}

		[FactWithTimeout]
		public async Task CanRescanFullyIndexedTransaction()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				await tester.Client.TrackAsync(pubkey);

				// In this test, we index a transaction, but miss an address (0/0 is found, but not 0/50 because it is outside the gap limit)
				tester.RPC.SendCommand(RPCOperations.sendmany, "",
						JObject.Parse($"{{ \"{tester.AddressOf(pubkey, "0/0")}\": \"0.9\", \"{tester.AddressOf(pubkey, "0/50")}\": \"0.5\" }}"));
				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForBlocks(tester.RPC.Generate(1));

				var transaction = tester.Client.GetTransactions(pubkey).ConfirmedTransactions.Transactions.Single();
				Assert.Single(transaction.Outputs);

				await tester.Client.ScanUTXOSetAsync(pubkey, 1000, 100);
				var info = WaitScanFinish(tester.Client, pubkey);
				Assert.Equal(2, info.Progress.Found);

				var aaa = await tester.Client.GetBalanceAsync(pubkey);
				// Rescanning should find 0/50
				transaction = (await tester.Client.GetTransactionsAsync(pubkey)).ConfirmedTransactions.Transactions.Single();
				Assert.Equal(2, transaction.Outputs.Count());

				await tester.RPC.EnsureGenerateAsync(1);
				tester.Notifications.WaitForBlocks(tester.RPC.Generate(1));

				// Check again
				transaction = (await tester.Client.GetTransactionsAsync(pubkey)).ConfirmedTransactions.Transactions.Single();
				Assert.Equal(2, transaction.Outputs.Count());
			}
		}

		[FactWithTimeout]
		public async Task CanScanUTXOSet()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);
				var utxo = tester.Client.GetUTXOs(pubkey); //Track things do not wait

				int gaplimit = 1000;
				int batchsize = 100;
				// By default, gap limit is 1000 and batch size is 100 on all 3 feature line
				var outOfBandAddress = pubkey.GetDerivation(new KeyPath("0/50"));
				var txId = tester.RPC.SendToAddress(outOfBandAddress.ScriptPubKey, Money.Coins(1.0m));
				Logs.Tester.LogInformation($"Sent money on 0/50 {txId}");
				tester.RPC.EnsureGenerate(1);
				tester.WaitSynchronized();

				// Nothing has been tracked because it is way out of bound and the first address is always unused
				var transactions = tester.Client.GetTransactions(pubkey);
				Assert.Empty(transactions.ConfirmedTransactions.Transactions);
				Assert.Equal(0, tester.Client.GetUnused(pubkey, DerivationFeature.Deposit).GetIndex(KeyPathTemplates.Default));

				// W00t! let's scan and see if it now appear in the UTXO
				tester.Client.ScanUTXOSet(pubkey, batchsize, gaplimit);
				var info = WaitScanFinish(tester.Client, pubkey);
				AssertExist(tester, pubkey, txId, true);
				Assert.Equal(100, info.Progress.CurrentBatchProgress);
				Assert.Equal(100, info.Progress.OverallProgress);
				Assert.Equal(1, info.Progress.Found);
				Assert.Equal(10, info.Progress.BatchNumber);
				Assert.Equal(0, info.Progress.RemainingBatches);
				Assert.Equal(1000, info.Progress.From);
				Assert.Equal(100, info.Progress.Count);
				Assert.Equal(50, info.Progress.HighestKeyIndexFound[DerivationFeature.Deposit]);
				Assert.Null(info.Progress.HighestKeyIndexFound[DerivationFeature.Change]);
				// Check that address 49 is tracked
				var scriptPubKey = pubkey.GetDerivation(new KeyPath("0/49")).ScriptPubKey;
#pragma warning disable CS0618 // Type or member is obsolete
				var infos = tester.Client.GetKeyInformations(scriptPubKey);
				Assert.Single(infos);
#pragma warning restore CS0618 // Type or member is obsolete

				Logs.Tester.LogInformation($"Check that the address pool has been emptied: 0/51 should be the next unused address");
				Assert.Equal(51, tester.Client.GetUnused(pubkey, DerivationFeature.Deposit).GetIndex(KeyPathTemplates.Default));
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Equal(txId, utxo.Confirmed.UTXOs[0].TransactionHash);

				Logs.Tester.LogInformation($"Check that the address pool has been emptied: 0/51 should be monitored, but not 0/150");
				Assert.NotNull(tester.Client.GetKeyInformation(pubkey, pubkey.GetDerivation(new KeyPath("0/51")).ScriptPubKey));
				Assert.Null(tester.Client.GetKeyInformation(pubkey, pubkey.GetDerivation(new KeyPath("0/150")).ScriptPubKey));

				Logs.Tester.LogInformation($"Let's check what happen if we scan a UTXO that is already fully indexed");
				outOfBandAddress = pubkey.GetDerivation(new KeyPath("0/51"));
				var txId2 = tester.RPC.SendToAddress(outOfBandAddress.ScriptPubKey, Money.Coins(1.0m));
				Logs.Tester.LogInformation($"Send money on 0/51 on {txId2}");
				tester.RPC.EnsureGenerate(1);
				tester.WaitSynchronized();
				Logs.Tester.LogInformation($"It should be indexed an unpruned");
				AssertExist(tester, pubkey, txId2, false);

				Logs.Tester.LogInformation($"It should be indexed an unpruned, even after a Scan happen");
				tester.Client.ScanUTXOSet(pubkey, batchsize, gaplimit);
				info = WaitScanFinish(tester.Client, pubkey);
				Assert.Equal(2, info.Progress.Found);
				AssertExist(tester, pubkey, txId2, false);

				Logs.Tester.LogInformation($"So finally we should have 2 UTXO, on 0/50 and 0/51");
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Equal(2, utxo.Confirmed.UTXOs.Count);
				Assert.NotEqual(NBitcoin.Utils.UnixTimeToDateTime(0), utxo.Confirmed.UTXOs[0].Timestamp);

				Logs.Tester.LogInformation($"Let's try to spend to ourselves");
				var changeAddress = tester.Client.GetUnused(pubkey, DerivationFeature.Change);
				var us = tester.Client.GetUnused(pubkey, DerivationFeature.Deposit);
				Assert.Equal(new KeyPath("0/52"), us.KeyPath);
				Assert.Equal(new KeyPath("1/0"), changeAddress.KeyPath);
				TransactionBuilder builder = tester.Network.CreateTransactionBuilder();
				builder.AddCoins(utxo.GetUnspentCoins());
				builder.AddKeys(utxo.GetKeys(key));
				builder.Send(us.ScriptPubKey, Money.Coins(1.1m));
				builder.SetChange(changeAddress.ScriptPubKey);
				var fallbackFeeRate = new FeeRate(Money.Satoshis(100), 1);
				var feeRate = tester.Client.GetFeeRate(1, fallbackFeeRate).FeeRate;
				builder.SendEstimatedFees(feeRate);
				var tx = builder.BuildTransaction(true);
				Assert.Equal(2, tx.Outputs.Count);
				Assert.True(tester.Client.Broadcast(tx).Success);
				tester.RPC.EnsureGenerate(1);
				AssertExist(tester, pubkey, tx.GetHash(), false);
				tester.Client.ScanUTXOSet(pubkey, batchsize, gaplimit);
				info = WaitScanFinish(tester.Client, pubkey);
				Assert.Equal(2, info.Progress.Found);
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Equal(2, utxo.GetUnspentUTXOs().Count());
				Assert.Contains(us.KeyPath, utxo.GetUnspentUTXOs().Select(u => u.KeyPath));
				Assert.Contains(changeAddress.KeyPath, utxo.GetUnspentUTXOs().Select(u => u.KeyPath));

				Logs.Tester.LogInformation($"Let's try to dump our server and rescan");
				tester.ResetExplorer();
				tester.Client.Track(pubkey);
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Empty(utxo.GetUnspentUTXOs());
				tester.Client.ScanUTXOSet(pubkey, batchsize, gaplimit);
				info = WaitScanFinish(tester.Client, pubkey);
				Assert.Equal(2, info.Progress.Found);
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Equal(2, utxo.GetUnspentUTXOs().Count());
				Assert.Contains(us.KeyPath, utxo.GetUnspentUTXOs().Select(u => u.KeyPath));
				Assert.Contains(changeAddress.KeyPath, utxo.GetUnspentUTXOs().Select(u => u.KeyPath));
				// But we lost the historic of the past transactions
				Assert.Single(tester.Client.GetTransactions(pubkey).ConfirmedTransactions.Transactions);

				tester.Client.Wipe(pubkey);
				Assert.Empty(tester.Client.GetTransactions(pubkey).ConfirmedTransactions.Transactions);
				tester.Client.ScanUTXOSet(pubkey, batchsize, gaplimit);
				info = WaitScanFinish(tester.Client, pubkey);
				Assert.Single(tester.Client.GetTransactions(pubkey).ConfirmedTransactions.Transactions);

				// Let's try to invalidate the blocks we indexed
				tester.Client.Wipe(pubkey);
				using var conn = await tester.GetService<Backend.DbConnectionFactory>().CreateConnection();
				await conn.ExecuteAsync("UPDATE blks SET confirmed='f';DELETE FROM blks;DELETE FROM txs;");

				// We should find the tx again
				Assert.Empty(tester.Client.GetTransactions(pubkey).ConfirmedTransactions.Transactions);
				tester.Client.ScanUTXOSet(pubkey, batchsize, gaplimit);
				info = WaitScanFinish(tester.Client, pubkey);
				Assert.Single(tester.Client.GetTransactions(pubkey).ConfirmedTransactions.Transactions);
			}
		}

		private ScanUTXOInformation WaitScanFinish(ExplorerClient client, DirectDerivationStrategy pubkey)
		{
			using var cts = new CancellationTokenSource(30_000);
			while (true)
			{
				var info = client.GetScanUTXOSetInformation(pubkey);
				// Small check to be sure we update overall progress correctly
				Assert.True(info.Progress.BatchNumber == 0 || info.Progress.OverallProgress != 0);
				if (info.Status == ScanUTXOStatus.Complete)
				{
					Assert.NotNull(info.Progress.CompletedAt);
					return info;
				}
				if (info.Status == ScanUTXOStatus.Error)
					Assert.Fail($"Scanning should not have failed {info.Error}");
				if (cts.IsCancellationRequested)
					Assert.Fail($"Scanning seems to be stuck (State: {info.Status})");
				Assert.Null(info.Progress.CompletedAt);
				Thread.Sleep(100);
			}
		}

		[Theory]
		[InlineData("*", "0", "1")]
		[InlineData("*/", "0", "1")]
		[InlineData("/*", "0", "1")]
		[InlineData("1/*", "1/0", "1/1")]
		[InlineData("1/*/2", "1/0/2", "1/1/2")]
		[InlineData("*/2", "0/2", "1/2")]
		[InlineData("m/*/2", "0/2", "1/2")]
		public void CanParseKeyPathTemplates(string template, string path1, string path2)
		{
			Assert.Equal(path1, KeyPathTemplate.Parse(template).GetKeyPath(0).ToString());
			Assert.Equal(path2, KeyPathTemplate.Parse(template).GetKeyPath(1).ToString());
		}

		[Fact]
		public void CanUseDerivationAdditionalOptions()
		{
			var network = GetNetwork(NBitcoin.Altcoins.AltNetworkSets.Liquid);
			var x = new ExtKey().Neuter().GetWif(network.NBitcoinNetwork);
			var plainXpub = network.DerivationStrategyFactory.Parse($"{x}");
			network.DerivationStrategyFactory.Parse($"{x}-[unblinded]");
			Assert.Throws<FormatException>(() => network.DerivationStrategyFactory.Parse($"{x}-[test]"));
			network.DerivationStrategyFactory.AuthorizedOptions.Add("test");
			network.DerivationStrategyFactory.AuthorizedOptions.Add("test1");
			network.DerivationStrategyFactory.AuthorizedOptions.Add("test2");
			var xpubTest = network.DerivationStrategyFactory.Parse($"{x}-[test]");
			var xpubTest2Args = network.DerivationStrategyFactory.Parse($"{x}-[test1]-[test2]");
			var xpubTest2ArgsInversed = network.DerivationStrategyFactory.Parse($"{x}-[TEST2]-[test1]");


			Assert.Empty(plainXpub.AdditionalOptions);

			Assert.NotEmpty(xpubTest.AdditionalOptions);
			Assert.True(xpubTest.AdditionalOptions.ContainsKey("test"));

			Assert.NotEqual(plainXpub, xpubTest2Args);
			Assert.NotEqual(xpubTest, xpubTest2Args);
			Assert.NotEmpty(xpubTest2Args.AdditionalOptions);

			Assert.True(xpubTest2Args.AdditionalOptions.ContainsKey("test1"));
			Assert.True(xpubTest2Args.AdditionalOptions.ContainsKey("test2"));

			Assert.Equal(xpubTest2Args, xpubTest2ArgsInversed);

			var xpub = network.DerivationStrategyFactory.Parse($"{x}-[TEST2]-[test1]-[p2sh]");
			Assert.Equal($"{x}-[p2sh]-[test1]-[test2]", xpub.ToString());
			Assert.Equal(2, xpub.AdditionalOptions.Count);
			Assert.True(xpub.AdditionalOptions.ContainsKey("test1"));
			Assert.True(xpub.AdditionalOptions.ContainsKey("test2"));
		}

		[FactWithTimeout]
		public async Task ElementsTests()
		{
			using (var tester = ServerTester.CreateNoAutoStart())
			{
				if (tester.Network.NetworkSet != NBitcoin.Altcoins.Liquid.Instance)
				{
					return;
				}
				tester.CreateWallet = true;
				tester.Start();
				var cashNode = tester.NodeBuilder.CreateNode(true);
				cashNode.Sync(tester.Explorer, true);
				var cashCow = cashNode.CreateRPCClient();
				tester.SendToAddress(cashCow.GetNewAddress(), Money.Coins(4.0m));
				var userDerivationScheme = tester.Client.GenerateWallet(new GenerateWalletRequest()
				{
					SavePrivateKeys = true,
					ImportKeysToRPC = true
				}).DerivationScheme;
				var blindedDerivationScheme = userDerivationScheme;
				//test: Elements shouldgenerate blinded addresses by default
				var address =
					Assert.IsType<BitcoinBlindedAddress>(tester.Client.GetUnused(userDerivationScheme,
						DerivationFeature.Deposit).Address);

				Assert.IsType<BitcoinBlindedAddress>(tester.Client.GetKeyInformation(userDerivationScheme, address.ScriptPubKey).Address);
				using (var session = await tester.Client.CreateWebsocketNotificationSessionAsync(Timeout))
				{
					await session.ListenAllTrackedSourceAsync(cancellation: Timeout);

					//test: Client should return Elements transaction types when event is published
					var evtTask = session.NextEventAsync(Timeout);
					var txid = await cashCow.SendToAddressAsync(address, Money.Coins(0.2m));
					var nodeTx = await cashCow.GetRawTransactionAsync(txid);
					var evt = Assert.IsType<NewTransactionEvent>(await evtTask);

					//test: Save correct tx hash with tx 
					Assert.Equal(txid, evt.TransactionData.TransactionHash);
					Assert.Equal(nodeTx.GetHash(), evt.TransactionData.Transaction.GetHash());
					var nbxTx = await tester.Client.GetTransactionAsync(txid);

					Assert.Equal(txid, nbxTx.TransactionHash);
					Assert.Equal(nodeTx.GetHash(), nbxTx.Transaction.GetHash());
					nbxTx.Transaction.PrecomputeHash(false, true);
					Assert.Equal(nodeTx.GetHash(), nbxTx.Transaction.GetHash());

					var fetched = await tester.Client.GetTransactionAsync(txid);

					Assert.Equal(nodeTx.GetHash(), fetched.Transaction.GetHash());
					fetched.Transaction.PrecomputeHash(false, true);
					Assert.Equal(nodeTx.GetHash(), fetched.Transaction.GetHash());
					Assert.Equal(nodeTx.GetHash(), fetched.TransactionHash);


					//test: Elements should have unblinded the outputs
					var output = Assert.Single(evt.Outputs);
					Assert.Equal(address, output.Address);
					var assetMoney = Assert.IsType<AssetMoney>(output.Value);
					Assert.Equal(Money.Coins(0.2m).Satoshi, assetMoney.Quantity);
					Assert.NotNull(assetMoney.AssetId);
					Assert.Equal(address, (await tester.Client.GetUTXOsAsync(userDerivationScheme)).Unconfirmed.UTXOs[0].Address);

					// but not the transaction itself
					var tx = Assert.IsAssignableFrom<ElementsTransaction>(evt.TransactionData.Transaction);
					Assert.Equal(txid, tx.GetHash());
					var elementsTxOut = Assert.IsAssignableFrom<ElementsTxOut>(tx.Outputs[output.Index]);
					Assert.Null(elementsTxOut.Value);
					//test: Get Transaction should give an ElementsTransaction
					tx = Assert.IsAssignableFrom<ElementsTransaction>((await tester.Client.GetTransactionAsync(txid)).Transaction);
					Assert.Equal(txid, tx.GetHash());

					//test: receive a tx to deriv scheme but to a confidential address with a different blinding key than our derivation method 
					evtTask = session.NextEventAsync(Timeout);
					txid = await cashCow.SendToAddressAsync(new BitcoinBlindedAddress(new Key().PubKey, address.UnblindedAddress), Money.Coins(2.0m));
					evt = Assert.IsType<NewTransactionEvent>(await evtTask);
					var unblindabletx = (Assert.IsAssignableFrom<ElementsTransaction>(Assert.IsType<NewTransactionEvent>(evt)
						.TransactionData.Transaction));
					Assert.Equal(txid, unblindabletx.GetHash());
					Assert.Contains(unblindabletx.Outputs, txout => Assert.IsAssignableFrom<ElementsTxOut>(txout).Value == null);

					output = Assert.Single(evt.Outputs);
					Assert.Equal(NBXplorerNetwork.UnknownAssetMoney, output.Value);

					var txInfos = tester.Client.GetTransactions(userDerivationScheme).UnconfirmedTransactions.Transactions;
					var assetMoney2 = Assert.IsType<AssetMoney>(Assert.Single(Assert.IsType<MoneyBag>(txInfos[1].BalanceChange)));
					Assert.Empty(Assert.IsType<MoneyBag>(txInfos[0].BalanceChange).Where(m => !m.IsUnknown()));

					Assert.Equal(assetMoney, assetMoney2);

					Thread.Sleep(1000);
					var received = tester.RPC.SendCommand("getreceivedbyaddress", address.ToString(), 0);
					var receivedMoney = received.Result["bitcoin"].Value<decimal>();

					Assert.Equal(0.2m, receivedMoney);

					Assert.DoesNotContain("-[unblinded]", userDerivationScheme.ToString());
					//test: setting up unblinded tracking
					userDerivationScheme = tester.NBXplorerNetwork.DerivationStrategyFactory.Parse(userDerivationScheme.ToString() + "-[unblinded]");

					Assert.Contains("-[unblinded]", userDerivationScheme.ToString());

					Assert.True(tester.NBXplorerNetwork.DerivationStrategyFactory.Parse(userDerivationScheme.ToString())
									.AdditionalOptions.TryGetValue("unblinded", out var unblinded));
					await tester.Client.TrackAsync(userDerivationScheme, Timeout);
					var unusedUnblinded = tester.Client.GetUnused(userDerivationScheme, DerivationFeature.Deposit);
					Assert.IsNotType<BitcoinBlindedAddress>(unusedUnblinded.Address);
					Assert.IsNotType<BitcoinBlindedAddress>(tester.Client.GetKeyInformation(userDerivationScheme, address.ScriptPubKey).Address);

					evtTask = session.NextEventAsync(Timeout);
					txid = await cashCow.SendToAddressAsync(unusedUnblinded.Address, Money.Coins(0.1m));
					evt = Assert.IsType<NewTransactionEvent>(await evtTask);
					tx = (Assert.IsAssignableFrom<ElementsTransaction>(Assert.IsType<NewTransactionEvent>(evt)
						.TransactionData.Transaction));
					Assert.Equal(txid, tx.GetHash());
					Assert.Contains(tx.Outputs, txout => Assert.IsAssignableFrom<ElementsTxOut>(txout).Value?.ToDecimal(MoneyUnit.BTC) == 0.1m);
				}
			}
		}

		private void RemoveUnknown(GetBalanceResponse b)
		{
			b.Available = new MoneyBag(((MoneyBag)b.Available).Where(i => !i.IsUnknown()).ToArray());
			b.Confirmed = new MoneyBag(((MoneyBag)b.Confirmed).Where(i => !i.IsUnknown()).ToArray());
			b.Immature = new MoneyBag(((MoneyBag)b.Immature).Where(i => !i.IsUnknown()).ToArray());
			b.Total = new MoneyBag(((MoneyBag)b.Total).Where(i => !i.IsUnknown()).ToArray());
			b.Unconfirmed = new MoneyBag(((MoneyBag)b.Unconfirmed).Where(i => !i.IsUnknown()).ToArray());
		}


		[Fact]
		public async Task CanGenerateWithRPCTracking()
		{
			using (var tester = ServerTester.CreateNoAutoStart())
			{
				tester.RPCWalletType = RPCWalletType.Descriptors;
				tester.Start();

				foreach (var scriptPubKeyType in new[]
				{
					ScriptPubKeyType.TaprootBIP86,
					ScriptPubKeyType.Legacy,
					ScriptPubKeyType.Segwit,
					ScriptPubKeyType.SegwitP2SH
				})
				{
					var derivation = (await tester.Client.GenerateWalletAsync(new GenerateWalletRequest()
					{
						SavePrivateKeys = true,
						ImportKeysToRPC = true,
						ScriptPubKeyType = scriptPubKeyType
					})).DerivationScheme;
					var addr1 = await tester.Client.GetUnusedAsync(derivation, DerivationFeature.Deposit);
					var mine = tester.RPC.SendCommand("getaddressinfo", addr1.Address.ToString())
						.Result["ismine"]
						.Value<bool>();
					Assert.True(mine);

					if (scriptPubKeyType == ScriptPubKeyType.TaprootBIP86)
					{
						// Try to generate more than one gap limit
						for (int i = 0; i < 10; i++)
						{
							await tester.Client.GetUnusedAsync(derivation, DerivationFeature.Deposit, reserve: true);
						}
						addr1 = await tester.Client.GetUnusedAsync(derivation, DerivationFeature.Deposit, reserve: true);
						mine = tester.RPC.SendCommand("getaddressinfo", addr1.Address.ToString())
						.Result["ismine"]
						.Value<bool>();
						Assert.True(mine);
					}
				}
				await Assert.ThrowsAsync<NBXplorerException>(() => tester.Client.GenerateWalletAsync(new GenerateWalletRequest()
				{
					SavePrivateKeys = false,
					ImportKeysToRPC = true,
					ScriptPubKeyType = ScriptPubKeyType.TaprootBIP86
				}));
			}
		}

		[TheoryWithTimeout]
		[InlineData(RPCWalletType.Descriptors)]
		[InlineData(RPCWalletType.Legacy)]
		public async Task CanGenerateWallet(RPCWalletType walletType)
		{
			using (var tester = ServerTester.CreateNoAutoStart())
			{
				tester.CreateWallet = true;
				tester.RPCWalletType = walletType;
				tester.Start();
				var cashNode = tester.NodeBuilder.CreateNode(true);
				cashNode.Sync(tester.Explorer, true);
				var cashCow = cashNode.CreateRPCClient();
				tester.SendToAddress(cashCow.GetNewAddress(), Money.Coins(4.0m));
				tester.RPC.Generate(1);

				Logs.Tester.LogInformation("Let's try default parameters");
				var wallet = await tester.Client.GenerateWalletAsync(new GenerateWalletRequest());
				Assert.NotNull(wallet.Mnemonic);
				Assert.NotNull(wallet.Passphrase);
				Assert.NotNull(wallet.WordList);
				var rootKey = wallet.GetMnemonic().DeriveExtKey(wallet.Passphrase);
				Assert.Equal(new RootedKeyPath(rootKey.GetPublicKey().GetHDFingerPrint(), new KeyPath("84'/1'/0'")), wallet.AccountKeyPath);
				Assert.Equal(WordCount.Twelve, wallet.WordCount);
				Assert.Equal(rootKey.Derive(wallet.AccountKeyPath).Neuter().GetWif(tester.Network).ToString(),
					wallet.DerivationScheme.ToString());

				foreach (var metadata in new[]
				{
					WellknownMetadataKeys.AccountHDKey,
					WellknownMetadataKeys.Mnemonic,
					WellknownMetadataKeys.MasterHDKey
				})
				{
					Assert.Null(await tester.Client.GetMetadataAsync<string>(wallet.DerivationScheme, metadata));
				}
				foreach (var metadata in new[]
				{
					WellknownMetadataKeys.AccountKeyPath,
					WellknownMetadataKeys.AccountDescriptor,
					WellknownMetadataKeys.ImportAddressToRPC
				})
				{
					Assert.NotNull(await tester.Client.GetMetadataAsync<string>(wallet.DerivationScheme, metadata));
				}

				Assert.Equal("False", await tester.Client.GetMetadataAsync<string>(wallet.DerivationScheme, WellknownMetadataKeys.ImportAddressToRPC));

				Logs.Tester.LogInformation("Let's make sure our parameters are not ignored");
				wallet = await tester.Client.GenerateWalletAsync(new GenerateWalletRequest()
				{
					ImportKeysToRPC = true,
					Passphrase = "hello",
					ScriptPubKeyType = ScriptPubKeyType.SegwitP2SH,
					WordCount = WordCount.Fifteen,
					WordList = Wordlist.French,
					AccountNumber = 2,
					SavePrivateKeys = true
				});
				Assert.StartsWith("sh(wpkh", wallet.AccountDescriptor);
				foreach (var metadata in new[]
				{
					WellknownMetadataKeys.AccountHDKey,
					WellknownMetadataKeys.Mnemonic,
					WellknownMetadataKeys.MasterHDKey,
					WellknownMetadataKeys.AccountKeyPath,
					WellknownMetadataKeys.ImportAddressToRPC
				})
				{
					Assert.NotNull(await tester.Client.GetMetadataAsync<string>(wallet.DerivationScheme, metadata));
				}

				Assert.NotNull(wallet.Mnemonic);
				Assert.Equal("hello", wallet.Passphrase);
				Assert.Equal(new RootedKeyPath(wallet.MasterHDKey.GetPublicKey().GetHDFingerPrint(), new KeyPath("49'/1'/2'")), wallet.AccountKeyPath);
				Assert.Equal(Wordlist.French.ToString(), wallet.WordList.ToString());
				Assert.Equal(WordCount.Fifteen, wallet.WordCount);
				var masterKey = new Mnemonic(wallet.Mnemonic.ToString(), wallet.WordList).DeriveExtKey(wallet.Passphrase);
				Assert.Equal(masterKey.GetPublicKey().GetHDFingerPrint(), wallet.AccountKeyPath.MasterFingerprint);
				Assert.Equal(masterKey.GetWif(tester.Network), wallet.MasterHDKey);
				Assert.Equal(masterKey.Derive(wallet.AccountKeyPath).Neuter().GetWif(tester.Network).ToString() + "-[p2sh]",
					wallet.DerivationScheme.ToString());
				var repo = tester.GetService<RepositoryProvider>().GetRepository(tester.Client.Network);
				Assert.Equal(wallet.DerivationScheme.GetExtPubKeys().Single().PubKey, wallet.AccountHDKey.GetPublicKey());
				Logs.Tester.LogInformation("Let's assert it is tracked");
				var firstKeyInfo = repo.GetKeyInformation(wallet.DerivationScheme.GetChild(new KeyPath("0/0")).GetDerivation().ScriptPubKey);
				Assert.NotNull(firstKeyInfo);
				var firstGenerated = await tester.Client.GetUnusedAsync(wallet.DerivationScheme, DerivationFeature.Deposit);
				Assert.Equal(firstKeyInfo.ScriptPubKey, firstGenerated.ScriptPubKey);
				await tester.Client.GetUnusedAsync(wallet.DerivationScheme, DerivationFeature.Deposit);

				Logs.Tester.LogInformation($"Let's assert it is tracked by RPC {firstKeyInfo.Address}");
				var rpc = tester.GetService<RPCClientProvider>().Get(tester.Client.Network);

				var txid = await cashCow.SendToAddressAsync(firstKeyInfo.Address, Money.Coins(1.01m));
				tester.Notifications.WaitForTransaction(wallet.DerivationScheme, txid);

				var money = await rpc.GetReceivedByAddressAsync(firstKeyInfo.Address, 0);
				Assert.Equal(Money.Coins(1.01m), money);
				var addressInfo = await rpc.GetAddressInfoAsync(firstKeyInfo.Address);
				Assert.True(addressInfo.IsMine);
				Assert.False(addressInfo.IsWatchOnly);

				Logs.Tester.LogInformation("Let's test the metadata are correct");
				Assert.Equal(wallet.MasterHDKey, await tester.Client.GetMetadataAsync<BitcoinExtKey>(wallet.DerivationScheme, WellknownMetadataKeys.MasterHDKey));
				Assert.Equal(wallet.AccountKeyPath, await tester.Client.GetMetadataAsync<RootedKeyPath>(wallet.DerivationScheme, WellknownMetadataKeys.AccountKeyPath));
				Assert.Equal(wallet.AccountHDKey, await tester.Client.GetMetadataAsync<BitcoinExtKey>(wallet.DerivationScheme, WellknownMetadataKeys.AccountHDKey));
				Assert.Equal(wallet.Mnemonic.ToString(), await tester.Client.GetMetadataAsync<string>(wallet.DerivationScheme, WellknownMetadataKeys.Mnemonic));

				var birthdate = DateTimeOffset.ParseExact(await tester.Client.GetMetadataAsync<string>(wallet.DerivationScheme, WellknownMetadataKeys.Birthdate), "O", CultureInfo.InvariantCulture);
				Assert.True(DateTimeOffset.UtcNow - birthdate < TimeSpan.FromSeconds(60));
				Assert.Equal(walletType == RPCWalletType.Descriptors ? "Descriptors" : "Legacy", await tester.Client.GetMetadataAsync<string>(wallet.DerivationScheme, WellknownMetadataKeys.ImportAddressToRPC));

				Logs.Tester.LogInformation("Let's check if psbt are properly rooted automatically");
				txid = await tester.SendToAddressAsync(firstGenerated.Address, Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(wallet.DerivationScheme, txid);
				var psbtResponse = await tester.Client.CreatePSBTAsync(wallet.DerivationScheme, new CreatePSBTRequest()
				{
					Destinations = new List<CreatePSBTDestination>()
					{
						new CreatePSBTDestination()
						{
							Amount = Money.Coins(1.0m),
							Destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network),
							SubstractFees = true,
							SweepAll = true
						}
					},
					FeePreference = new FeePreference()
					{
						FallbackFeeRate = new FeeRate(1.0m)
					}
				});

				var input = psbtResponse.PSBT.Inputs[0].HDKeyPaths.Single();
				Assert.Equal(wallet.AccountKeyPath.Derive(new KeyPath("0/0")).KeyPath, input.Value.KeyPath);
				Assert.Equal(wallet.AccountKeyPath.MasterFingerprint, input.Value.MasterFingerprint);
				Assert.Equal(firstGenerated.ScriptPubKey, input.Key.GetScriptPubKey(ScriptPubKeyType.SegwitP2SH));

				var generatedWallet = await tester.Client.GenerateWalletAsync(new GenerateWalletRequest());

				var importedWallet = await tester.Client.GenerateWalletAsync(new GenerateWalletRequest()
				{
					ExistingMnemonic = generatedWallet.Mnemonic,
					Passphrase = generatedWallet.Passphrase

				});

				Assert.Equal(generatedWallet.DerivationScheme, importedWallet.DerivationScheme);
				Assert.Equal(generatedWallet.AccountKeyPath, importedWallet.AccountKeyPath);
				Assert.Equal(generatedWallet.AccountHDKey, importedWallet.AccountHDKey);
				Assert.Equal(generatedWallet.MasterHDKey, importedWallet.MasterHDKey);
			}
		}
		[FactWithTimeout]
		public async Task CanUseRPCProxy()
		{
			using (var tester = ServerTester.Create())
			{
				Assert.NotNull(await tester.Client.RPCClient.GetBlockchainInfoAsync());

				var batchTest = tester.Client.RPCClient.PrepareBatch();
				var balanceResult = batchTest.GetBalanceAsync();
				var getblockhash = batchTest.GetBlockHashAsync(-1);
				var blockchainInfoResult = batchTest.GetBlockchainInfoAsync();
				await batchTest.SendBatchAsync();
				await balanceResult;
				await blockchainInfoResult;

				var rpcex = await Assert.ThrowsAsync<RPCException>(() => getblockhash);
				Assert.NotNull(rpcex.Message);
				Assert.Equal(RPCErrorCode.RPC_INVALID_PARAMETER, rpcex.RPCCode);

				tester.GetService<ExplorerConfiguration>().ChainConfigurations[0].ExposeRPC = false;

				// We shouldn't be able to query non whitelisted rpc methods
				var ex = await Assert.ThrowsAsync<HttpRequestException>(() => tester.Client.RPCClient.AbandonTransactionAsync(uint256.Zero));
				Assert.Equal((HttpStatusCode)401, ex.StatusCode);

				// Can't do in batch either...
				batchTest = tester.Client.RPCClient.PrepareBatch();
				balanceResult = batchTest.GetBalanceAsync();
				await batchTest.SendBatchAsync();
				ex = await Assert.ThrowsAsync<HttpRequestException>(() => balanceResult);
				Assert.Equal((HttpStatusCode)401, ex.StatusCode);

				// Should be OK, it's whitelisted
				await tester.Client.RPCClient.GetTxOutAsync(uint256.One, 0);
			}
		}


		[Fact]
		public async Task DoNotHangDuringReorg()
		{
			using var tester = ServerTester.Create();
			var wallet = await tester.Client.GenerateWalletAsync(new GenerateWalletRequest());
			var addr = await tester.Client.GetUnusedAsync(wallet.DerivationScheme, DerivationFeature.Deposit);
			var txId = tester.SendToAddress(addr.Address, Money.Coins(1.0m));
			tester.Notifications.WaitForTransaction(wallet.DerivationScheme, txId);
			var blocks = await tester.RPC.GenerateAsync(4);
			for (int i = 0; i < blocks.Length; i++)
			{
				Logs.Tester.LogInformation($"Chain1: [{i}]: {blocks[i]}");
			}
			tester.Notifications.WaitForBlocks(blocks[^1]);
			Logs.Tester.LogInformation("Invalidate the first block which confirmed the transaction " + blocks[0]);
			tester.RPC.InvalidateBlock(blocks[0]);
			var blocks2 = await tester.RPC.GenerateAsync(3);
			for (int i = 0; i < blocks2.Length; i++)
			{
				Logs.Tester.LogInformation($"Chain2: [{i}]: {blocks2[i]}");
			}
			tester.Notifications.WaitForBlocks(blocks2[^1]);
			Logs.Tester.LogInformation("Reconsider the block " + blocks[0]);
			tester.RPC.SendCommand("reconsiderblock", blocks[0]);

			Logs.Tester.LogInformation($"Waiting for the first chain to be processed again");
			tester.Notifications.WaitForBlocks(blocks[^1]);
		}


		[Fact]
		public async Task IsTrackedTests()
		{
			using var tester = ServerTester.Create();
			var xpub = new DerivationSchemeTrackedSource(new DirectDerivationStrategy(
				new BitcoinExtPubKey(new Mnemonic(Wordlist.English).DeriveExtKey().Neuter(), tester.Network), true));
			Assert.False(await tester.Client.IsTrackedAsync(xpub, Cancel));
			await tester.Client.TrackAsync(xpub, new TrackWalletRequest(), Cancel);
			Assert.True(await tester.Client.IsTrackedAsync(xpub, Cancel));
			
			var address = new AddressTrackedSource(new Key().GetAddress(ScriptPubKeyType.Legacy, tester.Network));
			Assert.False(await tester.Client.IsTrackedAsync(address, Cancel));
			await tester.Client.TrackAsync(address, new TrackWalletRequest(), Cancel);
			Assert.True(await tester.Client.IsTrackedAsync(address, Cancel));

			var group = new GroupTrackedSource("lolno");
			Assert.False(await tester.Client.IsTrackedAsync(group, Cancel));
			
			group = new GroupTrackedSource((await tester.Client.CreateGroupAsync(Cancel)).GroupId);
			Assert.True(await tester.Client.IsTrackedAsync(group, Cancel));
		}
	}
}
