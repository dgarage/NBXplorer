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
		public void CanSerializeKeyPathFast()
		{
			using (var tester = RepositoryTester.Create(true))
			{
				var dummy = new DirectDerivationStrategy(new ExtKey().Neuter().GetWif(Network.RegTest)) { Segwit = false };
				var seria = new Serializer(Network.RegTest);
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
			Assert.Equal(2, tester.Repository.RefillAddressPoolIfNeeded(dummy, DerivationFeature.Deposit, 2).Result);
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
			Assert.Equal(28, tester.Repository.RefillAddressPoolIfNeeded(dummy, DerivationFeature.Deposit).Result);
			Assert.Equal(30, tester.Repository.RefillAddressPoolIfNeeded(dummy, DerivationFeature.Change).Result);

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
				Assert.Equal(0, tester.Repository.RefillAddressPoolIfNeeded(dummy, DerivationFeature.Deposit).Result);
				MarkAsUsed(tester.Repository, dummy, new KeyPath("0/" + i));
			}
			keyInfo = tester.Repository.GetKeyInformation(dummy.GetLineFor(DerivationFeature.Deposit).Derive(30).ScriptPubKey);
			Assert.Null(keyInfo);
			MarkAsUsed(tester.Repository, dummy, new KeyPath("0/10"));
			Assert.Equal(11, tester.Repository.RefillAddressPoolIfNeeded(dummy, DerivationFeature.Deposit).Result);
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
						{ strat.Derive(keyPath).ScriptPubKey, keyPath }
					})}).GetAwaiter().GetResult();
		}

		[Fact]
		public void ShouldBlockIfNoChange()
		{
			using (var tester = ServerTester.Create())
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
		public void CanEasilySpendUTXOs()
		{
			using (var tester = ServerTester.Create())
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
				tester.SendToAddress(newAddress.ScriptPubKey, Money.Coins(1.0m));
				utxos = tester.Client.GetUTXOs(userDerivationScheme, utxos, true);

				// Send 1 more BTC
				newAddress = tester.Client.GetUnused(userDerivationScheme, DerivationFeature.Deposit);
				tester.SendToAddress(newAddress.ScriptPubKey, Money.Coins(1.0m));
				utxos = tester.Client.GetUTXOs(userDerivationScheme, utxos, true);

				utxos = tester.Client.GetUTXOs(userDerivationScheme, null, false);
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

					utxos = tester.Client.GetUTXOs(userDerivationScheme, utxos, true);
					utxos = tester.Client.GetUTXOs(userDerivationScheme, null, false);

					if (i == 0)
						Assert.Equal(2, utxos.GetUnspentCoins().Length);

					Assert.Contains(utxos.GetUnspentCoins(), u => u.ScriptPubKey == changeAddress.ScriptPubKey);
					Assert.Contains(utxos.Unconfirmed.UTXOs, u => u.ScriptPubKey == changeAddress.ScriptPubKey && u.Feature == DerivationFeature.Change);
				}
			}
		}

		[Fact]
		public void ShowRBFedTransaction()
		{
			using (var tester = ServerTester.Create())
			{
				var bob = tester.CreateDerivationStrategy();
				tester.Client.Track(bob);
				var utxo = tester.Client.GetUTXOs(bob, null, false); //Track things do not wait
				var a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);

				var payment1 = Money.Coins(0.04m);
				var payment2 = Money.Coins(0.08m);

				var tx1 = tester.RPC.SendToAddress(a1.ScriptPubKey, payment1, replaceable: true);

				utxo = tester.Client.GetUTXOs(bob, utxo); //Wait tx received
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
			using (var tester = ServerTester.Create())
			{
				var bob = tester.CreateDerivationStrategy();
				var utxo = tester.Client.GetUTXOs(bob, null, false); //Track things do not wait

				var a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);
				Assert.Null(a1);
				tester.Client.Track(bob);
				a1 = tester.Client.GetUnused(bob, DerivationFeature.Deposit, 0);
				Assert.NotNull(a1);
				Assert.NotNull(a1.Address);
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
				tester.SendToAddressAsync(a1.ScriptPubKey, Money.Coins(1.0m));
				utxo = tester.Client.GetUTXOs(bob, utxo); //Wait tx received

				tester.SendToAddressAsync(a3.ScriptPubKey, Money.Coins(1.0m));
				utxo = tester.Client.GetUTXOs(bob, utxo); //Wait tx received

				tester.SendToAddressAsync(a4.ScriptPubKey, Money.Coins(1.0m));
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
				new Serializer(Network.RegTest).ConfigureSerializer(settings);

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

				//Configure JSON custom serialization
				JsonSerializerSettings settings = new JsonSerializerSettings();
				new Serializer(Network.RegTest).ConfigureSerializer(settings);

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

				var utxo = tester.Client.GetUTXOs(pubkey, null, false);

				tester.RPC.SendCommand(RPCOperations.sendmany, "",
						JObject.Parse($"{{ \"{tester.AddressOf(pubkey, "0/1")}\": \"0.9\", \"{tester.AddressOf(pubkey, "0/0")}\": \"0.5\" }}"));
				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				tester.RPC.EnsureGenerate(1);
				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.Equal(2, utxo.Confirmed.UTXOs.Count);
				var fundingTxId = utxo.Confirmed.UTXOs[0].Outpoint.Hash;

				Logs.Tester.LogInformation("Let's spend one of the coins");
				// [funding, spending1]
				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/1"));
				var spending1 = tester.RPC.SendToAddress(new Key().PubKey.Hash.GetAddress(tester.Network), Money.Coins(0.1m));
				Logs.Tester.LogInformation($"Spent on {spending1}");
				// Let's add some transactions spending to push the spending in the first quarter
				// [funding, *spending1*, tx1, tx2, tx3, tx4]
				Thread.Sleep(1000);
				for (int i = 0; i < 4; i++)
				{
					tester.SendToAddress(tester.AddressOf(pubkey, "0"), Money.Coins(0.01m));
				}
				tester.RPC.EnsureGenerate(1);
				tester.WaitSynchronized();

				tester.Configuration.AutoPruningTime = TimeSpan.Zero; // Activate pruning


				Logs.Tester.LogInformation("After activating pruning, it still should not pruned, because there is still one coin");
				utxo = tester.Client.GetUTXOs(pubkey, null, false);
				utxo = tester.Client.GetUTXOs(pubkey, null, false);
				AssertNotPruned(tester, pubkey, fundingTxId);
				AssertNotPruned(tester, pubkey, spending1);

				Logs.Tester.LogInformation("Let's spend the other coin");
				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/0"));
				var spending2 = tester.RPC.SendToAddress(new Key().PubKey.Hash.GetAddress(tester.Network), Money.Coins(0.1m));
				Logs.Tester.LogInformation($"Spent on {spending2}");
				Thread.Sleep(1000);
				// Let's add some transactions spending to push the spending in the first quarter
				// [funding, spending1, tx1, tx2, tx3, tx4, *spending2*, tx21, tx22, ..., tx217]
				for (int i = 0; i < 17; i++)
				{
					if (i == 10)
						tester.RPC.EnsureGenerate(1); // Can't have too big chain on unconf
					tester.SendToAddress(tester.AddressOf(pubkey, "0"), Money.Coins(0.001m));
				}
				tester.RPC.EnsureGenerate(1);
				tester.WaitSynchronized();

				// Now it should get pruned
				Logs.Tester.LogInformation($"Now {spending1} and {spending2} should be pruned");
				utxo = tester.Client.GetUTXOs(pubkey, null);
				AssertPruned(tester, pubkey, fundingTxId);
				AssertPruned(tester, pubkey, spending1);
				AssertPruned(tester, pubkey, spending2);
			}
		}

		private static TransactionInformation AssertPruned(ServerTester tester, DerivationStrategyBase pubkey, uint256 txid)
		{
			TransactionInformation tx = AssertExist(tester, pubkey, txid);
			Assert.Null(tx.Transaction);
			return tx;
		}
		private static TransactionInformation AssertNotPruned(ServerTester tester, DerivationStrategyBase pubkey, uint256 txid)
		{
			TransactionInformation tx = AssertExist(tester, pubkey, txid);
			Assert.NotNull(tx.Transaction);
			return tx;
		}

		private static TransactionInformation AssertExist(ServerTester tester, DerivationStrategyBase pubkey, uint256 txid)
		{
			return AssertExist(tester, new DerivationSchemeTrackedSource(pubkey), txid);
		}
		private static TransactionInformation AssertExist(ServerTester tester, TrackedSource pubkey, uint256 txid)
		{
			int retry = 0;
			TransactionInformation tx = null;
			while (true)
			{
				retry++;
				var txs = tester.Client.GetTransactions(pubkey, null, false);
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

				var utxo = tester.Client.GetUTXOs(pubkey, null, false);

				tester.RPC.SendCommand(RPCOperations.sendmany, "",
						JObject.Parse($"{{ \"{tester.AddressOf(pubkey, "0/1")}\": \"0.9\", \"{tester.AddressOf(pubkey, "0/0")}\": \"0.5\" }}"));
				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				tester.RPC.EnsureGenerate(1);
				utxo = tester.Client.GetUTXOs(pubkey, utxo);
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
				LockTestCoins(tester.RPC, new HashSet<Script>());
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, coinDestination.KeyPath.ToString()));
				var spending2 = tester.RPC.SendToAddress(new Key().ScriptPubKey, Money.Coins(0.01m));
				Logs.Tester.LogInformation($"Spent again the coin in spending2({spending2})");
				var tx = tester.RPC.GetRawTransactionAsync(spending2).Result;
				Assert.Contains(tx.Inputs, (i) => i.PrevOut.Hash == spending1);

				// Let's add some transactions spending to push the spending2 in the first quarter
				// [funding, spending1, *spending2*, tx1, tx2, tx3, tx4, tx5]
				Thread.Sleep(1000);
				for (int i = 0; i < 5; i++)
				{
					tester.SendToAddress(tester.AddressOf(pubkey, "0"), Money.Coins(0.01m));
				}
				tester.RPC.EnsureGenerate(1);
				tester.WaitSynchronized();

				tester.Configuration.AutoPruningTime = TimeSpan.Zero; // Activate pruning

				// spending1 should not be pruned because fundingTx still can't be pruned
				Logs.Tester.LogInformation($"Spending spending1({spending1}) and spending2({spending2} can't be pruned, because a common ancestor fundingTx({fundingTxId}) can't be pruned");
				utxo = tester.Client.GetUTXOs(pubkey, null);
				AssertNotPruned(tester, pubkey, fundingTxId);
				AssertNotPruned(tester, pubkey, spending1);
				AssertNotPruned(tester, pubkey, spending2);

				// Let's spend the other coin of fundingTx
				Thread.Sleep(1000);
				LockTestCoins(tester.RPC, new HashSet<Script>());
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(key, "0/0"));
				var spending3 = tester.RPC.SendToAddress(new Key().PubKey.Hash.GetAddress(tester.Network), Money.Coins(0.1m));
				Logs.Tester.LogInformation($"Spent the second coin to 0/0 in spending3({spending3})");
				// Let's add some transactions spending to push the spending in the first quarter
				// [funding, spending1, spending2, tx1, tx2, tx3, tx4, tx5, *spending3*, tx21, tx22, ..., tx232]
				Thread.Sleep(1000);
				for (int i = 0; i < 32 - 9; i++)
				{
					if (i % 10 == 0)
						tester.RPC.EnsureGenerate(1); // Can't have too big chain on unconf
					tester.SendToAddress(tester.AddressOf(pubkey, "0"), Money.Coins(0.001m));
				}
				tester.RPC.EnsureGenerate(1);
				tester.WaitSynchronized();

				Logs.Tester.LogInformation($"Now fundingTx({fundingTxId}), spendgin1({spending1}) and spending2({spending2}) should be pruned");
				utxo = tester.Client.GetUTXOs(pubkey, null);
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
				using (var connected = tester.Client.CreateNotificationSession())
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

				using (var connected = tester.Client.CreateNotificationSession())
				{
					connected.ListenAllDerivationSchemes();
					tester.SendToAddress(tester.AddressOf(pubkey, "0/1"), Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.DerivationStrategy, pubkey);
				}

				using (var connected = tester.Client.CreateNotificationSession())
				{
					connected.ListenAllTrackedSource();
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
				using (var connected = tester.Client.CreateNotificationSession())
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
						var derived = ((DerivationSchemeTrackedSource)txEvent.TrackedSource).DerivationStrategy.Derive(output.KeyPath);
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

				var o = utxoAlice;
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
			using (var tester = ServerTester.Create())
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
			using (var tester = ServerTester.Create())
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

				Logs.Tester.LogInformation($"Creating a chain of 20 unconfirmed transaction...");
				int i = 0;
				// Reserve addresses ahead of time so that we are sure that the server is not too late to generate the next one
				for (i = 0; i < 20; i++)
				{
					tester.Client.GetUnused(pubkey, DerivationFeature.Deposit, reserve: true);
				}
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
				}

				while (true)
				{
					utxo = tester.Client.GetUTXOs(pubkey, utxo, true, Timeout);
					if (!utxo.HasChanges)
						continue;
					Assert.NotNull(utxo.Confirmed.KnownBookmark);
					Assert.True(utxo.Unconfirmed.HasChanges);
					Assert.Single(utxo.Unconfirmed.UTXOs);
					if (new KeyPath($"0/{i}").Equals(utxo.Unconfirmed.UTXOs[0].KeyPath))
						break;
				}

				tester.RPC.EnsureGenerate(1);

				utxo = tester.Client.GetUTXOs(pubkey, utxo, true, Timeout);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.True(utxo.Confirmed.HasChanges);
				Assert.Empty(utxo.Confirmed.SpentOutpoints);
			}
		}

		[Fact]
		public void CanUseWebSocketsOnAddress()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new Key();
				var pubkey = TrackedSource.Create(key.PubKey.GetAddress(tester.Network));
				tester.Client.Track(pubkey);
				using (var connected = tester.Client.CreateNotificationSession())
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

				using (var connected = tester.Client.CreateNotificationSession())
				{
					connected.ListenAllTrackedSource();
					tester.SendToAddress(pubkey.Address, Money.Coins(1.0m));

					var txEvent = (Models.NewTransactionEvent)connected.NextEvent(Cancel);
					Assert.Equal(txEvent.TrackedSource, pubkey);
				}
			}
		}

		[Fact]
		public void CanUseWebSocketsOnAddress2()
		{
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var key = new Key();
				var pubkey = TrackedSource.Create(key.PubKey.GetAddress(tester.Network));

				var key2 = new Key();
				var pubkey2 = TrackedSource.Create(key2.PubKey.GetAddress(tester.Network));

				tester.Client.Track(pubkey);
				tester.Client.Track(pubkey2);
				using (var connected = tester.Client.CreateNotificationSession())
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
				var pubkey = new DerivationStrategyFactory(extkey.Network).Parse($"{extkey.Neuter()}-[legacy]");
				var key = extkey.ExtKey.Derive(new KeyPath("0/0")).PrivateKey;
				var address = key.PubKey.GetAddress(tester.Network);
				var addressSource = TrackedSource.Create(address);
				tester.Client.Track(addressSource);
				var utxo = tester.Client.GetUTXOs(addressSource, null, false); //Track things do not wait

				var tx1 = tester.SendToAddress(address, Money.Coins(1.0m));
				utxo = tester.Client.GetUTXOs(addressSource, utxo);
				Assert.NotNull(utxo.Confirmed.KnownBookmark);
				Assert.Single(utxo.Unconfirmed.UTXOs);
				Assert.Equal(tx1, utxo.Unconfirmed.UTXOs[0].Outpoint.Hash);

				// The address has been only tracked individually, not via the extpubkey
				tester.Client.Track(pubkey);
				var unused = tester.Client.GetUnused(pubkey, DerivationFeature.Deposit);
				Assert.Equal(new KeyPath("0/0"), unused.KeyPath);
				Assert.Equal(address.ScriptPubKey, unused.ScriptPubKey);
				utxo = tester.Client.GetUTXOs(pubkey, null);
				Assert.Empty(utxo.Unconfirmed.UTXOs);

				// But this end up tracked once the block is mined
				tester.RPC.Generate(1);
				utxo = tester.Client.GetUTXOs(pubkey, utxo);
				Assert.NotNull(utxo.Confirmed.KnownBookmark);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(tx1, utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.NotNull(utxo.TrackedSource);
				Assert.NotNull(utxo.DerivationStrategy);
				var dsts = Assert.IsType<DerivationSchemeTrackedSource>(utxo.TrackedSource);
				Assert.Equal(utxo.DerivationStrategy, dsts.DerivationStrategy);

				// Make sure the transaction appear for address as well
				utxo = tester.Client.GetUTXOs(addressSource, null);
				Assert.Single(utxo.Confirmed.UTXOs);
				Assert.Equal(tx1, utxo.Confirmed.UTXOs[0].Outpoint.Hash);
				Assert.NotNull(utxo.TrackedSource);
				Assert.Null(utxo.DerivationStrategy);
				Assert.IsType<AddressTrackedSource>(utxo.TrackedSource);

				// Check it appear in transaction list
				var tx = tester.Client.GetTransactions(addressSource, null);
				Assert.Equal(tx1, tx.ConfirmedTransactions.Transactions[0].TransactionId);

				tx = tester.Client.GetTransactions(pubkey, null);
				Assert.Equal(tx1, tx.ConfirmedTransactions.Transactions[0].TransactionId);

				// Trying to send to a single address from a tracked extkey
				var extkey2 = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey2 = new DerivationStrategyFactory(extkey.Network).Parse($"{extkey.Neuter()}-[legacy]");
				tester.Client.Track(pubkey2);
				tester.SendToAddress(pubkey2.Derive(new KeyPath("0/0")).ScriptPubKey, Money.Coins(1.0m));

				utxo = tester.Client.GetUTXOs(addressSource, null);
				var utxo2 = tester.Client.GetUTXOs(pubkey2, null);
				LockTestCoins(tester.RPC);
				tester.RPC.ImportPrivKey(tester.PrivateKeyOf(extkey2, "0/0"));
				var tx2 = tester.SendToAddress(address, Money.Coins(0.6m));
				tester.RPC.EnsureGenerate(1);
				AssertExist(tester, addressSource, tx2);
				AssertExist(tester, pubkey2, tx2);
				utxo = tester.Client.GetUTXOs(addressSource, null);
				utxo2 = tester.Client.GetUTXOs(pubkey2, null);
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
			using (var tester = ServerTester.Create())
			{
				//WaitServerStarted not needed, just a sanity check
				var bob = tester.CreateDerivationStrategy();
				tester.Client.WaitServerStarted();
				tester.Client.Track(bob);
				var utxo = tester.Client.GetUTXOs(bob, null, false); //Track things do not wait

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
			var derivation = strategy.GetLineFor(DerivationFeature.Deposit).Derive(1);
			var derivation2 = strategy.Derive(DerivationStrategyBase.GetKeyPath(DerivationFeature.Deposit).Derive(1));
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
				tester.Client.WaitServerStarted(Timeout);
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());

				tester.Client.Track(pubkey);
				var utxo = tester.Client.GetUTXOs(pubkey, null, false); //Track things do not wait

				var txId = tester.SendToAddress(tester.AddressOf(key, "0/0"), Money.Coins(1.0m));
				var result = tester.Client.GetTransactions(pubkey, new[] { Bookmark.Start }, new[] { Bookmark.Start }, new[] { Bookmark.Start });
				Assert.True(result.HasChanges());
				Assert.Single(result.UnconfirmedTransactions.Transactions);
				// Sanity check that if we filter the transaction, we get only the expected one
				var tx1 = tester.Client.GetTransaction(pubkey, txId);
				Assert.NotNull(tx1);
				Assert.Equal(Money.Coins(1.0m), tx1.BalanceChange);
				Assert.Null(tester.Client.GetTransaction(pubkey, uint256.One));

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
				var notify = tester.Client.CreateNotificationSession();
				notify.ListenNewBlock();
				var blockId = tester.RPC.Generate(1)[0];
				var blockId2 = tester.RPC.Generate(1)[0];

				notify.NextEvent();
				tester.Client.Track(pubkey);

				var utxos = tester.Client.GetUTXOs(pubkey, null, false);
				Assert.Empty(utxos.Confirmed.UTXOs);

				for (int i = 0; i < 2; i++)
				{
					tester.Client.Rescan(new RescanRequest()
					{
						Transactions =
						{
							new RescanRequest.TransactionToRescan() { BlockId = blockId, TransactionId = txId1 },
							new RescanRequest.TransactionToRescan() { BlockId = blockId2, TransactionId = txId2 }, // should fail because wrong block
							new RescanRequest.TransactionToRescan() {  TransactionId = txId3 },  // should fail because no -txindex, but RPC remember wallet transactions :(
							new RescanRequest.TransactionToRescan() { BlockId = blockId, Transaction = tx4 },  // should find it
						}
					});

					utxos = tester.Client.GetUTXOs(pubkey, null, false);
					foreach (var txid in new[] { txId1, txId4, txId3 })
					{
						Assert.Contains(utxos.Confirmed.UTXOs, u => u.AsCoin().Outpoint.Hash == txid);
						var tx = tester.Client.GetTransaction(txid);
						Assert.Equal(2, tx.Confirmations);
					}
					Assert.Equal(3, tester.Client.GetTransactions(pubkey, null, false).ConfirmedTransactions.Transactions.Count);
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
		public void CanTrack()
		{
			using (var tester = ServerTester.Create())
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
			using (var tester = ServerTester.Create())
			{
				tester.Client.WaitServerStarted();
				var tx = tester.Network.Consensus.ConsensusFactory.CreateTransaction();
				tx.Outputs.Add(new TxOut(Money.Coins(1.0m), new Key()));
				var funded = tester.User1.CreateRPCClient().WithCapabilitiesOf(tester.RPC).FundRawTransaction(tx);
				var signed = tester.User1.CreateRPCClient().WithCapabilitiesOf(tester.RPC).SignRawTransaction(funded.Transaction);
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
		public void CanReserveDirectAddress()
		{
			using (var tester = ServerTester.Create())
			{
				//WaitServerStarted not needed, just a sanity check
				var bob = tester.CreateDerivationStrategy();
				tester.Client.WaitServerStarted();
				tester.Client.Track(bob);
				var utxo = tester.Client.GetUTXOs(bob, null, false); //Track things do not wait

				var tasks = new List<Task<KeyPathInformation>>();
				for (int i = 0; i < 10; i++)
				{
					tasks.Add(tester.Client.GetUnusedAsync(bob, DerivationFeature.Direct, reserve: true));
				}
				Task.WaitAll(tasks.ToArray());

				var paths = tasks.Select(t => t.Result).ToDictionary(c => c.KeyPath);
				Assert.Equal(9, paths.Select(p => p.Value.GetIndex()).Max());

				tester.Client.CancelReservation(bob, new[] { new KeyPath("0") });
				var path = tester.Client.GetUnused(bob, DerivationFeature.Direct).KeyPath;
				Assert.Equal(new KeyPath("0"), path);
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
				var script = pubkey.Derive(new KeyPath("0/0")).ScriptPubKey;

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

				var keyInfo = tester.Client.GetKeyInformation(pubkey, pubkey.Derive(new KeyPath("0/0")).ScriptPubKey);
				Assert.NotNull(keyInfo?.Address);
				Assert.Null(tester.Client.GetKeyInformation(pubkey, pubkey.Derive(new KeyPath("0/100")).ScriptPubKey));

				key = new BitcoinExtKey(new ExtKey(), tester.Network);
				pubkey = tester.CreateDerivationStrategy(key.Neuter());
				Assert.Null(tester.Client.GetKeyInformation(pubkey, pubkey.Derive(new KeyPath("0/0")).ScriptPubKey));
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
		public void CanScanUTXOSet()
		{
			using (var tester = ServerTester.Create())
			{
				var key = new BitcoinExtKey(new ExtKey(), tester.Network);
				var pubkey = tester.CreateDerivationStrategy(key.Neuter());
				tester.Client.Track(pubkey);
				var utxo = tester.Client.GetUTXOs(pubkey, null, false); //Track things do not wait

				int gaplimit = 1000;
				int batchsize = 100;
				// By default, gap limit is 1000 and batch size is 100 on all 3 feature line
				var outOfBandAddress = pubkey.Derive(new KeyPath("0/50"));
				var txId = tester.RPC.SendToAddress(outOfBandAddress.ScriptPubKey, Money.Coins(1.0m));
				Logs.Tester.LogInformation($"Sent money on 0/50 {txId}");
				tester.RPC.EnsureGenerate(1);
				tester.WaitSynchronized();

				// Nothing has been tracked because it is way out of bound and the first address is always unused
				var transactions = tester.Client.GetTransactions(pubkey, null, false);
				Assert.Empty(transactions.ConfirmedTransactions.Transactions);
				Assert.Equal(0, tester.Client.GetUnused(pubkey, DerivationFeature.Deposit).GetIndex());

				// W00t! let's scan and see if it now appear in the UTXO
				tester.Client.ScanUTXOSet(pubkey, batchsize, gaplimit);
				var info = WaitScanFinish(tester.Client, pubkey);
				AssertPruned(tester, pubkey, txId);
				Assert.Equal(100, info.Progress.CurrentBatchProgress);
				Assert.Equal(100, info.Progress.OverallProgress);
				Assert.Equal(1, info.Progress.Found);
				Assert.Equal(10, info.Progress.BatchNumber);
				Assert.Equal(0, info.Progress.RemainingBatches);
				Assert.Equal(1000, info.Progress.From);
				Assert.Equal(100, info.Progress.Count);
				Assert.Equal(50, info.Progress.HighestKeyIndexFound[DerivationFeature.Deposit]);
				Assert.Null(info.Progress.HighestKeyIndexFound[DerivationFeature.Change]);
				Logs.Tester.LogInformation($"Check that the address pool has been emptied: 0/51 should be the next unused address");
				Assert.Equal(51, tester.Client.GetUnused(pubkey, DerivationFeature.Deposit).GetIndex());
				utxo = tester.Client.GetUTXOs(pubkey, null, false);
				Assert.Equal(txId, utxo.Confirmed.UTXOs[0].TransactionHash);

				Logs.Tester.LogInformation($"Check that the address pool has been emptied: 0/51 should be monitored, but not 0/150");
				Assert.NotNull(tester.Client.GetKeyInformation(pubkey, pubkey.Derive(new KeyPath("0/51")).ScriptPubKey));
				Assert.Null(tester.Client.GetKeyInformation(pubkey, pubkey.Derive(new KeyPath("0/150")).ScriptPubKey));

				Logs.Tester.LogInformation($"Let's check what happen if we scan a UTXO that is already fully indexed");
				outOfBandAddress = pubkey.Derive(new KeyPath("0/51"));
				var txId2 = tester.RPC.SendToAddress(outOfBandAddress.ScriptPubKey, Money.Coins(1.0m));
				Logs.Tester.LogInformation($"Send money on 0/51 on {txId2}");
				tester.RPC.EnsureGenerate(1);
				tester.WaitSynchronized();
				Logs.Tester.LogInformation($"It should be indexed an unpruned");
				AssertNotPruned(tester, pubkey, txId2);

				Logs.Tester.LogInformation($"It should be indexed an unpruned, even after a Scan happen");
				tester.Client.ScanUTXOSet(pubkey, batchsize, gaplimit);
				info = WaitScanFinish(tester.Client, pubkey);
				Assert.Equal(2, info.Progress.Found);
				AssertNotPruned(tester, pubkey, txId2);

				Logs.Tester.LogInformation($"So finally we should have 2 UTXO, on 0/50 and 0/51");
				utxo = tester.Client.GetUTXOs(pubkey, null, false);
				Assert.Equal(2, utxo.Confirmed.UTXOs.Count);
				Assert.NotEqual(NBitcoin.Utils.UnixTimeToDateTime(0), utxo.Confirmed.UTXOs[0].Timestamp);
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

		[Fact]
		public void CanTrackDirect()
		{
			using (var tester = ServerTester.Create())
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
