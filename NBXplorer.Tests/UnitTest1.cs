using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.RPC;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin.Altcoins.Elements;
using Xunit;
using Xunit.Abstractions;

namespace NBXplorer.Tests
{
	public class UnitTest1
	{
		public UnitTest1(ITestOutputHelper helper)
		{
			Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
			Logs.LogProvider = new XUnitLogProvider(helper);
		}

		private NBXplorerNetwork GetNetwork(Network network)
		{
			return new NBXplorerNetwork(network.NetworkSet, network.NetworkType, new DerivationStrategyFactory(network));
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

		[Fact]
		public void RepositoryCanTrackAddresses()
		{
			using (var tester = RepositoryTester.Create(true))
			{
				var dummy = new DirectDerivationStrategy(new ExtKey().Neuter().GetWif(Network.RegTest)) { Segwit = false };
				RepositoryCanTrackAddressesCore(tester, dummy);
			}
		}

		[Fact]
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
				var dummy = new DirectDerivationStrategy(new ExtKey().Neuter().GetWif(Network.RegTest)) { Segwit = false };
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

		private static void RepositoryCanTrackAddressesCore(RepositoryTester tester, DerivationStrategyBase dummy)
		{
			Assert.Equal(2, tester.Repository.GenerateAddresses(dummy, DerivationFeature.Deposit, 2).Result);
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
			Assert.Equal(28, tester.Repository.GenerateAddresses(dummy, DerivationFeature.Deposit).Result);
			Assert.Equal(30, tester.Repository.GenerateAddresses(dummy, DerivationFeature.Change).Result);

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
			var tx = repository.Network.NBitcoinNetwork.Consensus.ConsensusFactory.CreateTransaction();
			repository.SaveMatches(new[] {
				new TrackedTransaction(
					new TrackedTransactionKey(tx.GetHash(), null, false),
					new DerivationSchemeTrackedSource(strat),
					tx,
					new Dictionary<Script, KeyPath>()
					{
						{ strat.GetDerivation(keyPath).ScriptPubKey, keyPath }
					})}).GetAwaiter().GetResult();
		}

		[Fact]
		public void CanEasilySpendUTXOs()
		{
			using (var tester = ServerTester.Create())
			{
				var userExtKey = new ExtKey();
				var userDerivationScheme = tester.Client.Network.DerivationStrategyFactory.CreateDirectDerivationStrategy(userExtKey.Neuter(), new DerivationStrategyOptions()
				{
					ScriptPubKeyType = ScriptPubKeyType.Legacy
				});
				tester.Client.Track(userDerivationScheme);

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

		[Fact]
		public void CanCreatePSBT()
		{
			using (var tester = ServerTester.Create())
			{
				CanCreatePSBTCore(tester, true);
				CanCreatePSBTCore(tester, false);
			}
		}

		private static void CanCreatePSBTCore(ServerTester tester, bool segwit)
		{
			var userExtKey = new ExtKey();
			var userDerivationScheme = tester.Client.Network.DerivationStrategyFactory.CreateDirectDerivationStrategy(userExtKey.Neuter(), new DerivationStrategyOptions()
			{
				ScriptPubKeyType = segwit ? ScriptPubKeyType.Segwit : ScriptPubKeyType.Legacy
			});
			tester.Client.Track(userDerivationScheme);
			var userExtKey2 = new ExtKey();
			var userDerivationScheme2 = tester.Client.Network.DerivationStrategyFactory.CreateDirectDerivationStrategy(userExtKey2.Neuter(), new DerivationStrategyOptions()
			{
				ScriptPubKeyType = segwit ? ScriptPubKeyType.Segwit : ScriptPubKeyType.Legacy
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
					}
				});

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

			psbt2 = tester.Client.CreatePSBT(userDerivationScheme, new CreatePSBTRequest()
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
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ReserveChangeAddress = false
			});

			var actualOutpoints = psbt2.PSBT.GetOriginalTransaction().Inputs.Select(i => i.PrevOut).ToArray();
			Assert.Single(actualOutpoints);
			Assert.Equal(outpoints[0], actualOutpoints[0]);

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
			Assert.True(txx.RBF);

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
				FeePreference = new FeePreference()
				{
					ExplicitFee = Money.Coins(0.000001m),
				},
				ReserveChangeAddress = false
			});
			Assert.Equal(3, psbt2.PSBT.Outputs.Count);
			Assert.Equal(2, psbt2.PSBT.Outputs.Where(o => o.HDKeyPaths.Any()).Count());
			Assert.Single(psbt2.PSBT.Outputs.Where(o => o.HDKeyPaths.Any(h => h.Value.KeyPath == newAddress.KeyPath)));

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
			
			Assert.All(expected.Inputs, i => Assert.Equal(segwit, i.NonWitnessUtxo == null));

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
					}
			});
			var globalXPub = psbt2.PSBT.GlobalXPubs[userDerivationScheme.GetExtPubKeys().First().GetWif(tester.Network)];
			Assert.Equal(new KeyPath("49'/0'"), globalXPub.KeyPath);

			Assert.Equal(3, psbt2.PSBT.Outputs.Count);
			Assert.Equal(2, psbt2.PSBT.Outputs.Where(o => o.HDKeyPaths.Any()).Count());
			var selfchange = Assert.Single(psbt2.PSBT.Outputs.Where(o => o.HDKeyPaths.Any(h => h.Key.GetAddress(segwit ? ScriptPubKeyType.Segwit : ScriptPubKeyType.Legacy, tester.Network).ScriptPubKey == newAddress.ScriptPubKey)));
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
		}

		[Fact]
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

				var tx1 = tester.RPC.SendToAddress(a1.ScriptPubKey, payment1, replaceable: true);
				tester.Notifications.WaitForTransaction(bob, tx1);
				var utxo = tester.Client.GetUTXOs(bob); //Wait tx received
				Assert.Equal(tx1, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);

				var tx = tester.RPC.GetRawTransaction(new uint256(tx1));
				foreach (var input in tx.Inputs)
				{
					input.ScriptSig = Script.Empty; //Strip signatures
				}
				var output = tx.Outputs.First(o => o.Value == payment1);
				output.Value = payment2;
				var change = tx.Outputs.First(o => o.Value != payment1);
				change.Value -= (payment2 - payment1) * 2; //Add more fees
				var replacement = tester.RPC.SignRawTransaction(tx);

				tester.RPC.SendRawTransaction(replacement);
				tester.Notifications.WaitForTransaction(bob, replacement.GetHash());
				var prevUtxo = utxo;
				utxo = tester.Client.GetUTXOs(bob); //Wait tx received
				Assert.Equal(replacement.GetHash(), utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				Assert.Single(utxo.Unconfirmed.UTXOs);

				var txs = tester.Client.GetTransactions(bob);
				Assert.Single(txs.UnconfirmedTransactions.Transactions);
				Assert.Equal(replacement.GetHash(), txs.UnconfirmedTransactions.Transactions[0].TransactionId);
				Assert.Single(txs.ReplacedTransactions.Transactions);
				Assert.Equal(tx1, txs.ReplacedTransactions.Transactions[0].TransactionId);

				Logs.Tester.LogInformation("Rebroadcasting the replaced TX should fail");
				var rebroadcaster = tester.GetService<RebroadcasterHostedService>();
				await rebroadcaster.RebroadcastPeriodically(tester.Client.Network, bobSource, new[] { tx1 });
				var rebroadcast = await rebroadcaster.RebroadcastAll();
				Assert.Single(rebroadcast.UnknownFailure);

				Logs.Tester.LogInformation("Rebroadcasting the replacement should succeed");
				await rebroadcaster.RebroadcastPeriodically(tester.Client.Network, bobSource, new[] { replacement.GetHash() });
				rebroadcast = await rebroadcaster.RebroadcastAll();
				Assert.Single(rebroadcast.Rebroadcasted); // Success

				tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));

				Logs.Tester.LogInformation("Rebroadcasting the replaced TX should clean one tx record (the unconf one) from the list");
				await rebroadcaster.RebroadcastPeriodically(tester.Client.Network, bobSource, new[] { replacement.GetHash() });
				rebroadcast = await rebroadcaster.RebroadcastAll();
				// The unconf record should be cleaned
				var cleaned = Assert.Single(rebroadcast.Cleaned);
				Assert.Null(cleaned.BlockHash);
				// Only one missing input, as there is only one txid
				Assert.Single(rebroadcast.MissingInputs);

				// Nothing should be cleaned now
				await rebroadcaster.RebroadcastPeriodically(tester.Client.Network, bobSource, new[] { replacement.GetHash() });
				rebroadcast = await rebroadcaster.RebroadcastAll();
				Assert.Empty(rebroadcast.Cleaned);

				Logs.Tester.LogInformation("Let's orphan the block, and check that the orphaned tx is cleaned");
				var orphanedBlock = tester.RPC.GetBestBlockHash();
				tester.RPC.InvalidateBlock(orphanedBlock);

				tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));

				await rebroadcaster.RebroadcastPeriodically(tester.Client.Network, bobSource, new[] { replacement.GetHash() });
				rebroadcast = await rebroadcaster.RebroadcastAll();
				cleaned = Assert.Single(rebroadcast.Cleaned);
				Assert.Equal(orphanedBlock, cleaned.BlockHash);
			}
		}

		[Fact]
		public void CanGetUnusedAddresses()
		{
			using (var tester = ServerTester.Create())
			{
				var bob = tester.CreateDerivationStrategy();
				var utxo = tester.Client.GetUTXOs(bob); //Track things do not wait

				var a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);
				Assert.Null(a1);
				tester.Client.Track(bob);
				a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);
				Assert.NotNull(a1);
				Assert.NotNull(a1.Address);
				Assert.Equal(a1.ScriptPubKey, tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0).ScriptPubKey);
				Assert.Equal(a1.ScriptPubKey, bob.GetDerivation(new KeyPath("0/0")).ScriptPubKey);

				var a2 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, skip: 1);
				Assert.Equal(a2.ScriptPubKey, bob.GetDerivation(new KeyPath("0/1")).ScriptPubKey);

				var a3 = tester.Client.GetUnused(bob, DerivationFeature.Change, skip: 0);
				Assert.Equal(a3.ScriptPubKey, bob.GetDerivation(new KeyPath("1/0")).ScriptPubKey);

				var a4 = tester.Client.GetUnused(bob, DerivationFeature.Direct, skip: 1);
				Assert.Equal(a4.ScriptPubKey, bob.GetDerivation(new KeyPath("1")).ScriptPubKey);

				Assert.Null(tester.Client.GetUnused(bob, DerivationFeature.Change, skip: 30));

				a3 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, skip: 2);
				Assert.Equal(new KeyPath("0/2"), a3.KeyPath);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
				//   0/0 and 0/2 used
				tester.SendToAddressAsync(a1.ScriptPubKey, Money.Coins(1.0m));
				tester.SendToAddressAsync(a3.ScriptPubKey, Money.Coins(1.0m));
				var txId = tester.SendToAddressAsync(a4.ScriptPubKey, Money.Coins(1.0m)).GetAwaiter().GetResult();
				tester.Notifications.WaitForTransaction(bob, txId);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
				a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);
				Assert.Equal(a1.ScriptPubKey, bob.GetDerivation(new KeyPath("0/1")).ScriptPubKey);
				a2 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, skip: 1);
				Assert.Equal(a2.ScriptPubKey, bob.GetDerivation(new KeyPath("0/3")).ScriptPubKey);

				a4 = tester.Client.GetUnused(bob, DerivationFeature.Direct, skip: 1);
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
				NBXplorerNetwork networkForDeserializion = new NBXplorerNetworkProvider(NetworkType.Regtest).GetFromCryptoCode((string)cryptoCode); 
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


		class TestMetadata
		{
			public string Message { get; set; }
		}
		[Fact]
		public void CanGetAndSetMetadata()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), true);
				tester.Client.Track(pubkey);

				Assert.Null(tester.Client.GetMetadata<TestMetadata>(pubkey, "test"));

				var expected = new TestMetadata() { Message = "hello" };
				tester.Client.SetMetadata(pubkey, "test", expected);

				var actual = tester.Client.GetMetadata<TestMetadata>(pubkey, "test");
				Assert.NotNull(actual);
				Assert.Equal(expected.Message, actual.Message);

				tester.Client.SetMetadata<TestMetadata>(pubkey, "test", null);
				Assert.Null(tester.Client.GetMetadata<TestMetadata>(pubkey, "test"));
			}
		}

		PruneRequest PruneTheMost = new PruneRequest() { DaysToKeep = 0.0 };
		[Fact]
		public void CanPrune()
		{
			// In this test we have fundingTxId with 2 output and spending1
			// We make sure that only once the 2 outputs of fundingTxId have been consumed
			// fundingTxId get pruned
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), true);
				tester.Client.Track(pubkey);
				tester.RPC.SendCommand(RPCOperations.sendmany, "",
						JObject.Parse($"{{ \"{tester.AddressOf(pubkey, "0/1")}\": \"0.9\", \"{tester.AddressOf(pubkey, "0/0")}\": \"0.5\" }}"));
				var utxo = tester.Client.GetUTXOs(pubkey);
				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForBlocks(tester.RPC.Generate(1));
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Equal(2, utxo.Confirmed.UTXOs.Count);
				var fundingTxId = utxo.Confirmed.UTXOs[0].Outpoint.Hash;
				Logs.Tester.LogInformation($"Funding tx ({fundingTxId}) has two coins");
				Logs.Tester.LogInformation("Let's spend one of the coins");
				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/1"));
				var spending1 = tester.RPC.SendToAddress(new Key().PubKey.Hash.GetAddress(tester.Network), Money.Coins(0.1m));
				Logs.Tester.LogInformation($"Spent on {spending1}");
				tester.RPC.EnsureGenerate(1);
				tester.WaitSynchronized();
				tester.Client.Prune(pubkey, PruneTheMost);

				Logs.Tester.LogInformation("It still should not pruned, because there is still another UTXO in funding tx");
				utxo = tester.Client.GetUTXOs(pubkey);
				utxo = tester.Client.GetUTXOs(pubkey);
				AssertNotPruned(tester, pubkey, fundingTxId);
				AssertNotPruned(tester, pubkey, spending1);

				Logs.Tester.LogInformation("Let's spend the other coin");
				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/0"));
				var spending2 = tester.RPC.SendToAddress(new Key().PubKey.Hash.GetAddress(tester.Network), Money.Coins(0.1m));
				Logs.Tester.LogInformation($"Spent on {spending2}");

				tester.RPC.EnsureGenerate(3);
				tester.WaitSynchronized();
				Logs.Tester.LogInformation($"Now {spending1} and {spending2} should be pruned if we want to keep 1H of blocks");
				tester.Client.Prune(pubkey, new PruneRequest() { DaysToKeep = 1.0/24.0 });
				AssertNotPruned(tester, pubkey, fundingTxId);
				AssertNotPruned(tester, pubkey, spending1);
				AssertNotPruned(tester, pubkey, spending2);

				tester.RPC.Generate(4);
				var totalPruned = tester.Client.Prune(pubkey, new PruneRequest() { DaysToKeep = 1.0 / 24.0 }).TotalPruned;
				Assert.Equal(3, totalPruned);
				totalPruned = tester.Client.Prune(pubkey, new PruneRequest() { DaysToKeep = 1.0 / 24.0 }).TotalPruned;
				Assert.Equal(0, totalPruned);
				Logs.Tester.LogInformation($"But after 1H of blocks, it should be pruned");
				utxo = tester.Client.GetUTXOs(pubkey);
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
				Assert.False(true, $"Transaction {txid} should exists");
			}
			if (shouldBePruned && tx.Transaction != null)
				Assert.False(true, $"Transaction {txid} should be pruned");
			if (!shouldBePruned && tx.Transaction == null)
				Assert.False(true, $"Transaction {txid} should not be pruned");
			return tx;
		}

		[Fact]
		public void CanPrune2()
		{
			// In this test we have fundingTxId with 2 output and spending1
			// We make sure that if only 1 outputs of fundingTxId have been consumed
			// spending1 does not get pruned, even if its output got consumed
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), true);
				tester.Client.Track(pubkey);

				var utxo = tester.Client.GetUTXOs(pubkey);

				tester.RPC.SendCommand(RPCOperations.sendmany, "",
						JObject.Parse($"{{ \"{tester.AddressOf(pubkey, "0/1")}\": \"0.9\", \"{tester.AddressOf(pubkey, "0/0")}\": \"0.5\" }}"));
				utxo = tester.Client.GetUTXOs(pubkey);
				tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Equal(2, utxo.Confirmed.UTXOs.Count);
				var fundingTxId = utxo.Confirmed.UTXOs[0].Outpoint.Hash;
				Logs.Tester.LogInformation($"Sent funding tx fundingTx({fundingTxId}) to 0/1 and 0/0");

				// Let's spend one of the coins of funding and spend it again
				// [funding, spending1, spending2]
				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/1"));
				var coinDestination = tester.Client.GetUnused(pubkey, DerivationFeature.Deposit);
				var coinDestinationAddress = coinDestination.ScriptPubKey;
				var spending1 = tester.RPC.SendToAddress(coinDestinationAddress, Money.Coins(0.1m));
				Logs.Tester.LogInformation($"Spent the coin to 0/1 in spending1({spending1})");
				tester.Notifications.WaitForTransaction(pubkey, spending1);
				LockTestCoins(tester.RPC, new HashSet<Script>());
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, coinDestination.KeyPath.ToString()));
				var spending2 = tester.RPC.SendToAddress(new Key().ScriptPubKey, Money.Coins(0.01m));
				tester.Notifications.WaitForTransaction(pubkey, spending2);
				Logs.Tester.LogInformation($"Spent again the coin in spending2({spending2})");
				var tx = tester.RPC.GetRawTransactionAsync(spending2).Result;
				Assert.Contains(tx.Inputs, (i) => i.PrevOut.Hash == spending1);
				tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));
				tester.WaitSynchronized();

				tester.Client.Prune(pubkey, PruneTheMost);
				// spending1 should not be pruned because fundingTx still can't be pruned
				Logs.Tester.LogInformation($"Spending spending1({spending1}) and spending2({spending2} can't be pruned, because a common ancestor fundingTx({fundingTxId}) can't be pruned");
				utxo = tester.Client.GetUTXOs(pubkey);
				AssertNotPruned(tester, pubkey, fundingTxId);
				AssertNotPruned(tester, pubkey, spending1);
				AssertNotPruned(tester, pubkey, spending2);

				// Let's spend the other coin of fundingTx
				Thread.Sleep(1000);
				LockTestCoins(tester.RPC, new HashSet<Script>());
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/0"));
				var spending3 = tester.RPC.SendToAddress(new Key().PubKey.Hash.GetAddress(tester.Network), Money.Coins(0.1m));
				tester.Notifications.WaitForTransaction(pubkey, spending3);
				Logs.Tester.LogInformation($"Spent the second coin to 0/0 in spending3({spending3})");
				tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));
				tester.WaitSynchronized();
				tester.Client.Prune(pubkey, PruneTheMost);

				Logs.Tester.LogInformation($"Now fundingTx({fundingTxId}), spendgin1({spending1}) and spending2({spending2}) should be pruned");
				utxo = tester.Client.GetUTXOs(pubkey);
				AssertPruned(tester, pubkey, fundingTxId);
				AssertPruned(tester, pubkey, spending1);
				AssertPruned(tester, pubkey, spending2);
			}
		}

		[Fact]
		public void CanUseWebSockets()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), true);
				tester.Client.Track(pubkey);
				using (var connected = tester.Client.CreateWebsocketNotificationSession())
				{
					connected.ListenNewBlock();
					var expectedBlockId = tester.Explorer.CreateRPCClient().Generate(1)[0];
					var blockEvent = (Models.NewBlockEvent)connected.NextEvent(Cancel);
					Assert.Equal(expectedBlockId, blockEvent.Hash);
					Assert.NotEqual(0, blockEvent.Height);

					connected.ListenDerivationSchemes(new[] { pubkey });
					tester.SendToAddress(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}

				using (var connected = tester.Client.CreateWebsocketNotificationSession())
				{
					connected.ListenAllDerivationSchemes();
					tester.SendToAddress(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}

				using (var connected = tester.Client.CreateWebsocketNotificationSession())
				{
					connected.ListenAllTrackedSource();
					tester.SendToAddress(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}
			}
		}

		[Fact]
		public void CanUseLongPollingNotifications()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), true);
				tester.Client.Track(pubkey);
				var connected = tester.Client.CreateLongPollingNotificationSession();
				{
					var expectedBlockId = tester.Explorer.CreateRPCClient().Generate(1)[0];
					var blockEvent = (Models.NewBlockEvent)connected.NextEvent(Cancel);
					Assert.Equal(expectedBlockId, blockEvent.Hash);
					Assert.NotEqual(0, blockEvent.Height);
					tester.SendToAddress(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}

				connected = tester.Client.CreateLongPollingNotificationSession(connected.LastEventId);
				{
					tester.SendToAddress(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}

				connected = tester.Client.CreateLongPollingNotificationSession(connected.LastEventId);
				{
					tester.SendToAddress(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}
			}
		}

		[Fact]
		public void CanUseWebSockets2()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter(), false);

				var pubkey2 = tester.CreateDerivationStrategy(key.Neuter(), true);

				tester.Client.Track(pubkey);
				tester.Client.Track(pubkey2);
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
					Assert.Contains(txEvent.DerivationStrategy.ToString(), schemes);
					schemes.Remove(txEvent.DerivationStrategy.ToString());

					if (!tester.RPC.Capabilities.SupportSegwit)
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
			using (var tester = ServerTester.Create())
			{
				var bob = new BitcoinExtKey(new ExtKey(), tester.Network);
				var alice = new BitcoinExtKey(new ExtKey(), tester.Network);

				var bobPubKey = tester.CreateDerivationStrategy(bob.Neuter());
				var alicePubKey = tester.CreateDerivationStrategy(alice.Neuter());

				tester.Client.Track(alicePubKey);
				var utxoAlice = tester.Client.GetUTXOs(alicePubKey);
				tester.Client.Track(bobPubKey);
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
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(alice, "0/1"));
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

		[Fact]
		public void CanTrack3()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);
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
				var utxo = tester.Client.GetUTXOs(pubkey);

				tester.RPC.EnsureGenerate(1);
				events.NextEvent(Timeout);
				events.NextEvent(Timeout);
				events.NextEvent(Timeout);
				events.NextEvent(Timeout);
				events.NextEvent(Timeout);

				Logs.Tester.LogInformation("Did we received 5 UTXOs?");
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Equal(5, utxo.Confirmed.UTXOs.Count);
			}
		}

		[Fact]
		public void CanTrackSeveralTransactions()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);

				var addresses = new HashSet<Script>();
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/0"));
				var id = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(pubkey, id);
				addresses.Add(tester.AddressOf(key, "0/0").ScriptPubKey);

				var utxo = tester.Client.GetUTXOs(pubkey);

				var coins = Money.Coins(1.0m);

				Logs.Tester.LogInformation($"Creating a chain of 20 unconfirmed transaction...");
				int i = 0;
				// Reserve addresses ahead of time so that we are sure that the server is not too late to generate the next one
				for (i = 0; i < 20; i++)
				{
					tester.Client.GetUnused(pubkey, DerivationFeature.Deposit, reserve: true);
				}
				uint256 lastTx = null;
				for (i = 0; i < 20; i++)
				{
					LockTestCoins(tester.RPC, addresses);
					var spendable = tester.RPC.ListUnspent(0, 0);
					coins = coins - Money.Coins(0.001m);
					var path = $"0/{i + 1}";
					var destination = tester.AddressOf(key, path);
					tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, path));
					var txId = tester.SendToAddress(destination, coins);
					Logs.Tester.LogInformation($"Sent to {path} in {txId}");
					addresses.Add(destination.ScriptPubKey);
					lastTx = txId;
				}

				tester.Notifications.WaitForTransaction(pubkey, lastTx);
				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForTransaction(pubkey, lastTx);

				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(lastTx, utxo.Confirmed.UTXOs[0].TransactionHash);
			}
		}

		[Fact]
		public void CanUseWebSocketsOnAddress()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
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

		[Fact]
		public void CanBatch()
		{
			var input = Enumerable.Range(0, 100);
			var outputs = input.Batch(30).ToArray();

			Assert.Equal(4, outputs.Length);
			Assert.True(outputs[0].SequenceEqual(Enumerable.Range(0, 30)));
			Assert.True(outputs[1].SequenceEqual(Enumerable.Range(30, 30)));
			Assert.True(outputs[2].SequenceEqual(Enumerable.Range(60, 30)));
			Assert.True(outputs[3].SequenceEqual(Enumerable.Range(90, 10)));

			input = Enumerable.Range(0, 90);
			outputs = input.Batch(30).ToArray();

			Assert.Equal(3, outputs.Length);
			Assert.True(outputs[0].SequenceEqual(Enumerable.Range(0, 30)));
			Assert.True(outputs[1].SequenceEqual(Enumerable.Range(30, 30)));
			Assert.True(outputs[2].SequenceEqual(Enumerable.Range(60, 30)));
		}

		[Fact]
		public void CanUseWebSocketsOnAddress2()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new Key();
				var pubkey = TrackedSource.Create(key.PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network));

				var key2 = new Key();
				var pubkey2 = TrackedSource.Create(key2.PubKey.GetAddress(ScriptPubKeyType.Legacy, tester.Network));

				tester.Client.Track(pubkey);
				tester.Client.Track(pubkey2);
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

		[Fact]
		public void CanTrackAddress()
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
				var utxo = tester.Client.GetUTXOs(addressSource);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(tx1, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);

				Logs.Tester.LogInformation("Let's make sure hd pubkey 0/0 is not tracked because we were not traking it when we broadcasted");
				tester.Client.Track(pubkey);
				var unused = tester.Client.GetUnused(pubkey, DerivationFeature.Deposit);
				Assert.Equal(new KeyPath("0/0"), unused.KeyPath);
				Assert.Equal(address.ScriptPubKey, unused.ScriptPubKey);
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Empty(utxo.Unconfirmed.UTXOs);

				Logs.Tester.LogInformation("But this end up tracked once the block is mined");
				tester.RPC.Generate(1);
				tester.Notifications.WaitForTransaction(pubkey, tx1);
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(tx1, utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.NotNull(utxo.TrackedSource);
				Assert.NotNull(utxo.DerivationStrategy);
				var dsts = Assert.IsType<DerivationSchemeTrackedSource>(utxo.TrackedSource);
				Assert.Equal(utxo.DerivationStrategy, dsts.DerivationStrategy);

				Logs.Tester.LogInformation("Make sure the transaction appear for tracked address as well");
				utxo = tester.Client.GetUTXOs(addressSource);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(tx1, utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.NotNull(utxo.TrackedSource);
				Assert.Null(utxo.DerivationStrategy);
				Assert.IsType<AddressTrackedSource>(utxo.TrackedSource);

				Logs.Tester.LogInformation("Check it appear in transaction list");
				var tx = tester.Client.GetTransactions(addressSource);
				Assert.Equal(tx1, tx.ConfirmedTransactions.Transactions[0].TransactionId);

				tx = tester.Client.GetTransactions(pubkey);
				Assert.Equal(tx1, tx.ConfirmedTransactions.Transactions[0].TransactionId);

				Logs.Tester.LogInformation("Trying to send to a single address from a tracked extkey");
				var extkey2 = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey2 = tester.NBXplorerNetwork.DerivationStrategyFactory.Parse($"{extkey.Neuter()}-[legacy]");
				tester.Client.Track(pubkey2);
				var txId = tester.SendToAddress(pubkey2.GetDerivation(new KeyPath("0/0")).ScriptPubKey, Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(pubkey2, txId);

				Logs.Tester.LogInformation("Sending from 0/0 to the tracked address");
				utxo = tester.Client.GetUTXOs(addressSource);
				var utxo2 = tester.Client.GetUTXOs(pubkey2);
				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(extkey2, "0/0"));
				var tx2 = tester.SendToAddress(address, Money.Coins(0.6m));
				tester.Notifications.WaitForTransaction(address, tx2);
				tester.RPC.EnsureGenerate(1);
				AssertExist(tester, addressSource, tx2, false);
				AssertExist(tester, pubkey2, tx2, false);
				utxo = tester.Client.GetUTXOs(addressSource);
				utxo2 = tester.Client.GetUTXOs(pubkey2);
				Assert.NotEmpty(utxo.Confirmed.UTXOs);
				Assert.NotEmpty(utxo2.Confirmed.UTXOs);
				Assert.Contains(utxo2.Confirmed.UTXOs, u => u.TransactionHash == tx2);
				Assert.Contains(utxo.Confirmed.UTXOs, u => u.TransactionHash == tx2);
				Assert.Null(utxo.Confirmed.UTXOs[0].Feature);
				Assert.NotNull(utxo2.Confirmed.UTXOs[0].Outpoint);
			}
		}

		[Fact]
		public void CanTrack2()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);
				Logs.Tester.LogInformation("Let's send 1.0BTC to 0/0");
				var tx00 = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(pubkey, tx00);
				var utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(tx00, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);


				Logs.Tester.LogInformation("Let's send 0.6BTC from 0/0 to 1/0");
				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/0"));
				var tx2 = tester.SendToAddress(tester.AddressOf(key, "1/0"), Money.Coins(0.6m));
				tester.Notifications.WaitForTransaction(pubkey, tx2);

				Logs.Tester.LogInformation("Should have 1 unconf UTXO of 0.6BTC");
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(tx2, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash); //got the 0.6m
				Assert.Equal(Money.Coins(0.6m), utxo.Unconfirmed.UTXOs[0].Value); //got the 0.6m
				Assert.Empty(utxo.Unconfirmed.SpentOutpoints);

				Logs.Tester.LogInformation("Let's send 0.15BTC to 0/0");
				var txid = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(0.15m));
				tester.Notifications.WaitForTransaction(pubkey, txid);

				Logs.Tester.LogInformation("0.15BTC and 0.6BTC should be in our UTXO");
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Equal(2, utxo.Unconfirmed.UTXOs.Count);
				Assert.IsType<Coin>(utxo.Unconfirmed.UTXOs[0].AsCoin(pubkey));
				Assert.Equal(Money.Coins(0.6m) + Money.Coins(0.15m), utxo.Unconfirmed.UTXOs[0].Value.Add(utxo.Unconfirmed.UTXOs[1].Value));
				Assert.Empty(utxo.Unconfirmed.SpentOutpoints);
			}
		}

		[Fact]
		public void CanReserveAddress()
		{
			using (var tester = ServerTester.Create())
			{
				//WaitServerStarted not needed, just a sanity check
				var bob = tester.CreateDerivationStrategy();
				tester.Client.WaitServerStarted();
				tester.Client.Track(bob);

				var tasks = new List<Task<KeyPathInformation>>();
				for (int i = 0; i < 100; i++)
				{
					tasks.Add(tester.Client.GetUnusedAsync(bob, DerivationFeature.Deposit, reserve: true));
				}
				Task.WaitAll(tasks.ToArray());

				var paths = tasks.Select(t => t.Result).ToDictionary(c => c.KeyPath);
				Assert.Equal(99, paths.Select(p => p.Value.GetIndex()).Max());

				tester.Client.CancelReservation(bob, new[] { new KeyPath("0/0") });
				Assert.Equal(new KeyPath("0/0"), tester.Client.GetUnused(bob, DerivationFeature.Deposit).KeyPath);
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
		}

		private static Derivation Generate(DerivationStrategyBase strategy)
		{
			var derivation = strategy.GetLineFor(KeyPathTemplates.Default.GetKeyPathTemplate(DerivationFeature.Deposit)).Derive(1U);
			var derivation2 = strategy.GetDerivation(KeyPathTemplates.Default.GetKeyPathTemplate(DerivationFeature.Deposit).GetKeyPath(1U));
			Assert.Equal(derivation.Redeem, derivation2.Redeem);
			return derivation;
		}

		[Fact]
		public void CanGetStatus()
		{
			using (var tester = ServerTester.Create())
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
				Assert.NotNull(status.BitcoinStatus.Capabilities);
			}
		}

		public CancellationToken Timeout => new CancellationTokenSource(10000).Token;


		[Fact]
		public void CanGetTransactionsOfDerivation()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);

				Logs.Tester.LogInformation("Let's send 1.0BTC to 0/0");
				var txId = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(pubkey, txId);
				Logs.Tester.LogInformation("Check if the tx exists");
				var result = tester.Client.GetTransactions(pubkey);
				Assert.Single(result.UnconfirmedTransactions.Transactions);

				var height = result.Height;
				var timestampUnconf = result.UnconfirmedTransactions.Transactions[0].Timestamp;
				Assert.Null(result.UnconfirmedTransactions.Transactions[0].BlockHash);
				Assert.Null(result.UnconfirmedTransactions.Transactions[0].Height);
				Assert.Equal(0, result.UnconfirmedTransactions.Transactions[0].Confirmations);
				Assert.Equal(result.UnconfirmedTransactions.Transactions[0].Transaction.GetHash(), result.UnconfirmedTransactions.Transactions[0].TransactionId);
				Assert.Equal(Money.Coins(1.0m), result.UnconfirmedTransactions.Transactions[0].BalanceChange);

				Logs.Tester.LogInformation("Sanity check that if we filter the transaction, we get only the expected one");
				var tx1 = tester.Client.GetTransaction(pubkey, txId);
				Assert.NotNull(tx1);
				Assert.Equal(Money.Coins(1.0m), tx1.BalanceChange);
				Assert.Null(tester.Client.GetTransaction(pubkey, uint256.One));

				tester.Client.IncludeTransaction = false;
				result = tester.Client.GetTransactions(pubkey);
				Assert.Null(result.UnconfirmedTransactions.Transactions[0].Transaction);

				Logs.Tester.LogInformation("Let's mine and send 1.0BTC to 0");
				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForTransaction(pubkey, txId);
				result = tester.Client.GetTransactions(pubkey);
				var txId2 = tester.SendToAddress(tester.AddressOf(key, "0"), Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(pubkey, txId2);
				Logs.Tester.LogInformation("We should now have two transactions");
				result = tester.Client.GetTransactions(pubkey);
				Assert.Single(result.ConfirmedTransactions.Transactions);
				Assert.Single(result.UnconfirmedTransactions.Transactions);
				Assert.Equal(txId2, result.UnconfirmedTransactions.Transactions[0].TransactionId);
				Assert.Equal(txId, result.ConfirmedTransactions.Transactions[0].TransactionId);
				Assert.Equal(timestampUnconf, result.ConfirmedTransactions.Transactions[0].Timestamp);

				Logs.Tester.LogInformation("Let's send from 0/0 to 0/1");
				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/0"));
				var txId3 = tester.SendToAddress(tester.AddressOf(key, "0/1"), Money.Coins(0.2m));
				tester.Notifications.WaitForTransaction(pubkey, txId3);
				result = tester.Client.GetTransactions(pubkey);
				Assert.Equal(2, result.UnconfirmedTransactions.Transactions.Count);
				Assert.Equal(Money.Coins(-0.8m), result.UnconfirmedTransactions.Transactions[0].BalanceChange);
				var tx3 = tester.Client.GetTransaction(pubkey, txId3);
				Assert.Equal(Money.Coins(-0.8m), tx3.BalanceChange);
			}
		}

		[Fact]
		public void CanTrack5()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);

				Logs.Tester.LogInformation("Send 1.0BTC to 0/0");
				var fundingTx = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				var utxo = tester.Client.GetUTXOs(pubkey);
				tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Single(utxo.Confirmed.UTXOs);

				Logs.Tester.LogInformation("Send 0.2BTC from the 0/0 to a random address");
				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/0"));
				var spendingTx = tester.SendToAddress(new Key().PubKey.Hash.GetAddress(tester.Network), Money.Coins(0.2m));
				tester.Notifications.WaitForTransaction(pubkey, spendingTx);
				Logs.Tester.LogInformation("Check we have empty UTXO as unconfirmed");
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Single(utxo.Unconfirmed.SpentOutpoints);
				Assert.Equal(fundingTx, utxo.Unconfirmed.SpentOutpoints[0].Hash);
				tester.Notifications.WaitForBlocks(tester.RPC.EnsureGenerate(1));

				Logs.Tester.LogInformation("Let's check if direct addresses can be tracked by sending to 0");
				var address = tester.Client.GetUnused(pubkey, DerivationFeature.Direct);
				Assert.Equal(DerivationFeature.Direct, address.Feature);
				fundingTx = tester.SendToAddress(tester.AddressOf(key, "0"), Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(pubkey, fundingTx);
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Equal(address.ScriptPubKey, utxo.Unconfirmed.UTXOs[0].ScriptPubKey);
				var address2 = tester.Client.GetUnused(pubkey, DerivationFeature.Direct);
				Assert.Equal(new KeyPath(1), address2.KeyPath);
			}
		}

		[Fact]
		public void CanRescan()
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
				tester.Client.Track(pubkey);

				var utxos = tester.Client.GetUTXOs(pubkey);
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

					utxos = tester.Client.GetUTXOs(pubkey);
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

		[Fact]
		public void CanTrackManyAddressesAtOnce()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());

				tester.Client.Track(pubkey, new TrackWalletRequest()
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
				var info = tester.Client.GetKeyInformations(pubkey.GetDerivation(new KeyPath("0/499")).ScriptPubKey);
				Assert.Single(info);
#pragma warning restore CS0618 // Type or member is obsolete
			}
		}

		[Fact]
		public void CanTrack()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());

				tester.Client.Track(pubkey);
				Logs.Tester.LogInformation("Sending 1.0 BTC to 0/0");
				var txId = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				tester.Notifications.WaitForTransaction(pubkey, txId);
				Logs.Tester.LogInformation("Making sure the BTC is properly received");
				var utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Equal(tester.Network.Consensus.CoinbaseMaturity + 1, utxo.CurrentHeight);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(txId, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				var unconfTimestamp = utxo.Unconfirmed.UTXOs[0].Timestamp;
				Assert.Equal(0, utxo.Unconfirmed.UTXOs[0].Confirmations);
				Assert.Empty(utxo.Confirmed.UTXOs);

				Logs.Tester.LogInformation("Making sure we can query the transaction");
				var tx = tester.Client.GetTransaction(utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);
				Assert.NotNull(tx);
				Assert.Equal(0, tx.Confirmations);
				Assert.Null(tx.BlockId);
				Assert.Equal(utxo.Unconfirmed.UTXOs[0].Outpoint.Hash, tx.Transaction.GetHash());
				Assert.Equal(unconfTimestamp, tx.Timestamp);

				Logs.Tester.LogInformation("Let's mine and wait for notification");
				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForTransaction(pubkey, txId);
				Logs.Tester.LogInformation("Let's see if our UTXO is properly confirmed");
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Empty(utxo.Unconfirmed.UTXOs);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(txId, utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.Equal(1, utxo.Confirmed.UTXOs[0].Confirmations);
				Assert.Equal(unconfTimestamp, utxo.Confirmed.UTXOs[0].Timestamp);

				Logs.Tester.LogInformation("Let's send 1.0 BTC to 0/1");
				var confTxId = txId;
				txId = tester.SendToAddress(tester.AddressOf(key, "0/1"), Money.Coins(1.0m));
				var txId01 = txId;
				tester.Notifications.WaitForTransaction(pubkey, txId);

				Logs.Tester.LogInformation("Let's see if we have both: an unconf UTXO and a conf one");
				utxo = tester.Client.GetUTXOs(pubkey);
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
				tx = tester.Client.GetTransaction(utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.NotNull(tx);
				Assert.Equal(unconfTimestamp, tx.Timestamp);
				Assert.Equal(1, tx.Confirmations);
				Assert.NotNull(tx.BlockId);
				Assert.Equal(utxo.Confirmed.UTXOs[0].Outpoint.Hash, tx.Transaction.GetHash());

				Logs.Tester.LogInformation("Let's mine, we should not have 2 confirmed UTXO");
				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForTransaction(pubkey, txId);
				utxo = tester.Client.GetUTXOs(pubkey);
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
				utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Equal(3, utxo.Confirmed.UTXOs.Count);
				Assert.Equal(new KeyPath("0/0"), utxo.Confirmed.UTXOs[0].KeyPath);
				Assert.Equal(new KeyPath("0/1"), utxo.Confirmed.UTXOs[1].KeyPath);
				Assert.Equal(new KeyPath("0/2"), utxo.Confirmed.UTXOs[2].KeyPath);

				tx = tester.Client.GetTransaction(tx.Transaction.GetHash());
				Assert.Equal(3, tx.Confirmations);
				Assert.NotNull(tx.BlockId);

				Logs.Tester.LogInformation("Let's send 0.5 BTC from 0/1 to 0/3");
				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/1"));
				txId = tester.SendToAddress(tester.AddressOf(key, "0/3"), Money.Coins(0.5m));
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
				txId = tester.SendToAddress(new Key().ScriptPubKey, Money.Coins(1.0m));
				Assert.NotNull(tester.Client.GetTransaction(txId));
				var blockId = tester.Explorer.Generate(1);
				tester.Notifications.WaitForBlocks(blockId);
				var savedTx = tester.Client.GetTransaction(txId);
				Assert.Equal(blockId[0], savedTx.BlockId);
			}
		}
		[Fact]
		public void CanCacheTransactions()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				Logs.Tester.LogInformation("Let's check an unconf miss result get properly cached: Let's send coins to 0/1 before tracking it");
				tester.RPC.Generate(1);
				var txId = tester.SendToAddress(tester.AddressOf(key, "0/1"), Money.Coins(1.0m));
				Logs.Tester.LogInformation($"Sent {txId}");
				Thread.Sleep(1000);
				tester.Client.Track(pubkey);
				Logs.Tester.LogInformation($"Tracked, let's mine");
				tester.Notifications.WaitForBlocks(tester.RPC.Generate(1));
				var utxo = tester.Client.GetUTXOs(pubkey);
				Assert.Empty(utxo.Confirmed.UTXOs);
				Assert.Empty(utxo.Unconfirmed.UTXOs);
			}
		}
		[Fact(Timeout = 60 * 1000)]
		public void CanUseLongPollingOnEvents()
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
				using (var cts = new CancellationTokenSource(1000))
				{
					try
					{
						evts = session.GetEvents(lastId, longPolling: true, cancellation: cts.Token);
						Assert.False(true, "Should throws");
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
				evts = gettingEvts.GetAwaiter().GetResult();
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
				Assert.Equal(2, evts.Length);
				Assert.IsType<Models.NewBlockEvent>(evts[0]);
				Assert.IsType<Models.NewTransactionEvent>(evts[1]);
			}
		}
		private void LockTestCoins(RPCClient rpc, HashSet<Script> keepAddresses = null)
		{
			if (keepAddresses == null)
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
		public void CanTopologicalSortRecords()
		{
			var key = new BitcoinExtKey(new ExtKey(), Network.RegTest);
			var pubkey = GetNetwork(Network.RegTest).DerivationStrategyFactory.Parse($"{key.Neuter().ToString()}");
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
			tx1.Record.SpentOutpoints.Add(outpoint);
			tx2.Record.ReceivedCoins.Add(new Coin(outpoint, new TxOut()));
			AssertExpectedOrder(new[] { tx2, tx1 }, true); // tx1 depends on tx2 so even if tx1 has been seen first, topological sort should be used
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

			bool IsEqual(AnnotatedTransaction tx, TrackedTransactionBuilder.TransactionContext ctx)
			{
				return tx.Record.TransactionHash == ctx._TransactionId && tx.Record.Inserted == ctx._TimeStamp;
			}

			for (int iii = 0; iii < 100; iii++)
			{
				NBitcoin.Utils.Shuffle(trackedTransactions);
				var collection = new AnnotatedTransactionCollection(trackedTransactions, builder._TrackedSource, chain, Network.RegTest);
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
			for (int iii = 0; iii < 100; iii++)
			{
				NBitcoin.Utils.Shuffle(trackedTransactions);
				var collection = new AnnotatedTransactionCollection(trackedTransactions, builder._TrackedSource, chain, Network.RegTest);
				Assert.Empty(collection.ReplacedTransactions);
				Assert.Empty(collection.UnconfirmedTransactions);
				Assert.Equal(3, collection.CleanupTransactions.Count);
				foreach (var dup in new[] { _17b3b3, _17b3b3dup, ab3922dup })
				{
					Assert.Contains(collection.CleanupTransactions, t => IsEqual(t, dup));
				}
			}
		}

		[Fact]
		public void CanBroadcast()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var tx = tester.Network.Consensus.ConsensusFactory.CreateTransaction();
				tx.Outputs.Add(Money.Coins(1.0m), new Key());
				var funded = tester.RPC.FundRawTransaction(tx);
				var signed = tester.RPC.SignRawTransaction(funded.Transaction);
				var result = tester.Client.Broadcast(signed);
				Assert.True(result.Success);
				signed.Inputs[0].PrevOut.N = 999;
				result = tester.Client.Broadcast(signed);
				Assert.False(result.Success);

				var ex = Assert.Throws<NBXplorerException>(() => tester.Client.GetFeeRate(5));
				Assert.Equal("fee-estimation-unavailable", ex.Error.Code);
			}
		}

		[Fact]
		public void CanGetKeyInformations()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);

				KeyPathInformation[] keyinfos;
				var script = pubkey.GetDerivation(new KeyPath("0/0")).ScriptPubKey;

#pragma warning disable CS0618 // Type or member is obsolete
				keyinfos = tester.Client.GetKeyInformations(script);
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

				var keyInfo = tester.Client.GetKeyInformation(pubkey, pubkey.GetDerivation(new KeyPath("0/0")).ScriptPubKey);
				Assert.NotNull(keyInfo?.Address);
				Assert.Null(tester.Client.GetKeyInformation(pubkey, pubkey.GetDerivation(new KeyPath("0/100")).ScriptPubKey));

				key = new BitcoinExtKey(new ExtKey(), tester.Network);
				pubkey = tester.CreateDerivationStrategy(key.Neuter());
				Assert.Null(tester.Client.GetKeyInformation(pubkey, pubkey.GetDerivation(new KeyPath("0/0")).ScriptPubKey));
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

		[Fact]
		public void CanRescanFullyIndexedTransaction()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);

				// In this test, we index a transaction, but miss an address (0/0 is found, but not 0/50 because it is outside the gap limit)
				tester.RPC.SendCommand(RPCOperations.sendmany, "",
						JObject.Parse($"{{ \"{tester.AddressOf(pubkey, "0/0")}\": \"0.9\", \"{tester.AddressOf(pubkey, "0/50")}\": \"0.5\" }}"));
				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForBlocks(tester.RPC.Generate(1));

				var transaction = tester.Client.GetTransactions(pubkey).ConfirmedTransactions.Transactions.Single();
				Assert.Single(transaction.Outputs);

				tester.Client.ScanUTXOSet(pubkey, 1000, 100);
				var info = WaitScanFinish(tester.Client, pubkey);
				Assert.Equal(2, info.Progress.Found);

				// Rescanning should find 0/50
				transaction = tester.Client.GetTransactions(pubkey).ConfirmedTransactions.Transactions.Single();
				Assert.Equal(2, transaction.Outputs.Count());

				tester.RPC.EnsureGenerate(1);
				tester.Notifications.WaitForBlocks(tester.RPC.Generate(1));

				// Check again
				transaction = tester.Client.GetTransactions(pubkey).ConfirmedTransactions.Transactions.Single();
				Assert.Equal(2, transaction.Outputs.Count());
			}
		}

		[Fact]
		public void CanScanUTXOSet()
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
				Assert.Equal(0, tester.Client.GetUnused(pubkey, DerivationFeature.Deposit).GetIndex());

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
				Assert.Equal(51, tester.Client.GetUnused(pubkey, DerivationFeature.Deposit).GetIndex());
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
			}
		}

		private ScanUTXOInformation WaitScanFinish(ExplorerClient client, DirectDerivationStrategy pubkey)
		{
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
					Assert.False(true, $"Scanning should not have failed {info.Error}");
				Assert.Null(info.Progress.CompletedAt);
				Thread.Sleep(100);
			}
		}

		[Theory]
		[InlineData("*","0", "1")]
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
		public async Task ElementsTests()
		{
			using (var tester = ServerTester.Create())
			{
				if (tester.Network.NetworkSet != NBitcoin.Altcoins.Liquid.Instance)
				{
					return;
				}
				var userDerivationScheme = tester.Client.GenerateWallet(new GenerateWalletRequest()
				{
					SavePrivateKeys = true,
					ImportKeysToRPC= true
				}).DerivationScheme;
				await tester.Client.TrackAsync(userDerivationScheme, Cancel);
				
				//test: Elements shouldgenerate blinded addresses by default
				var address =
					Assert.IsType<BitcoinBlindedAddress>(tester.Client.GetUnused(userDerivationScheme,
						DerivationFeature.Deposit).Address);


				using (var session = await tester.Client.CreateWebsocketNotificationSessionAsync(Timeout))
				{
					await session.ListenAllTrackedSourceAsync(cancellation: Timeout);

					//test: Client should return Elements transaction types when event is published
					var evtTask = session.NextEventAsync(Timeout);
					var txid = await tester.SendToAddressAsync(address, Money.Coins(1.0m));

					var evt = Assert.IsType<NewTransactionEvent>(await evtTask);


					//test: Elements should have unblinded the outputs
					var output = Assert.Single(evt.Outputs);
					var assetMoney = Assert.IsType<AssetMoney>(output.Value);
					Assert.Equal(Money.Coins(1.0m).Satoshi, assetMoney.Quantity);
					Assert.NotNull(assetMoney.AssetId);

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
					txid = await tester.SendToAddressAsync(new BitcoinBlindedAddress(new Key().PubKey, address.UnblindedAddress), Money.Coins(2.0m));
					evt = Assert.IsType<NewTransactionEvent>(await evtTask);
					var unblindabletx = (Assert.IsAssignableFrom<ElementsTransaction>(Assert.IsType<NewTransactionEvent>(evt)
						.TransactionData.Transaction));
					Assert.Equal(txid, unblindabletx.GetHash());
					Assert.Contains(unblindabletx.Outputs, txout => Assert.IsAssignableFrom<ElementsTxOut>(txout).Value == null);

					//test: The ouptut of the event should have null value
					output = Assert.Single(evt.Outputs);
					Assert.Null(output.Value);

					var txInfos = tester.Client.GetTransactions(userDerivationScheme).UnconfirmedTransactions.Transactions;
					var assetMoney2 = Assert.IsType<AssetMoney>(Assert.Single(Assert.IsType<MoneyBag>(txInfos[1].BalanceChange)));
					Assert.Empty(Assert.IsType<MoneyBag>(txInfos[0].BalanceChange));
					Assert.Equal(assetMoney, assetMoney2);

					tester.RPC.Generate(6);
					var received = tester.RPC.SendCommand("getreceivedbyaddress", address.ToString());
					var receivedMoney = received.Result["bitcoin"].Value<decimal>();
					// Assert.Equal(1.0m, receivedMoney);
					// Note that you would expect to have only 1.0 here because you would
					// expect the second 2.0 to not be unblindable by RPC
					// but because RPC originated this transaction, it can unblind it without knowing the blinding key
					Assert.Equal(3.0m, receivedMoney);
				}
			}
		}


		[Fact]
		public async Task CanGenerateWallet()
		{
			using (var tester = ServerTester.Create())
			{
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
					WellknownMetadataKeys.ImportAddressToRPC
				})
				{
					Assert.NotNull(await tester.Client.GetMetadataAsync<string>(wallet.DerivationScheme, metadata));
				}

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
				var waiter = tester.GetService<BitcoinDWaiters>().GetWaiter(tester.Client.Network);
				await Task.Delay(1000);
				var rpcAddressInfo = await waiter.RPC.GetAddressInfoAsync(firstKeyInfo.Address);
				Assert.True(rpcAddressInfo.IsMine);
				Assert.False(rpcAddressInfo.IsWatchOnly);


				Logs.Tester.LogInformation("Let's test the metadata are correct");
				Assert.Equal(wallet.MasterHDKey, await tester.Client.GetMetadataAsync<BitcoinExtKey>(wallet.DerivationScheme, WellknownMetadataKeys.MasterHDKey));
				Assert.Equal(wallet.AccountKeyPath, await tester.Client.GetMetadataAsync<RootedKeyPath>(wallet.DerivationScheme, WellknownMetadataKeys.AccountKeyPath));
				Assert.Equal(wallet.AccountHDKey, await tester.Client.GetMetadataAsync<BitcoinExtKey>(wallet.DerivationScheme, WellknownMetadataKeys.AccountHDKey));
				Assert.Equal(wallet.Mnemonic.ToString(), await tester.Client.GetMetadataAsync<string>(wallet.DerivationScheme, WellknownMetadataKeys.Mnemonic));
				Assert.Equal("True", await tester.Client.GetMetadataAsync<string>(wallet.DerivationScheme, WellknownMetadataKeys.ImportAddressToRPC));

				Logs.Tester.LogInformation("Let's check if psbt are properly rooted automatically");
				var txid = await tester.SendToAddressAsync(firstGenerated.Address, Money.Coins(1.0m));
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
	}
}
