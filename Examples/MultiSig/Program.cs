using NBitcoin;
using NBitcoin.RPC;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace MultiSig
{
    class Program
    {
        class Party
        {
            public Party(Mnemonic mnemonic, string password, KeyPath accountKeyPath)
            {
                // Note: you could just generate the ExtKey with new ExtKey() and save extKey.GetWif(network) somewhere.
                // But saving a mnemonic + password is well known UX
                Mnemonic = mnemonic;
                PartyName = password; //lazy yes
                RootExtKey = mnemonic.DeriveExtKey(password);
                AccountExtPubKey = RootExtKey.Derive(accountKeyPath).Neuter();

                // The AccountKeyPath should be stored along the AccountExtPubKey
                // This is the keypath + the hash of the root hd key.
                // During signing, NBitcoin need this information to derive the RootExtKey to the address keypath properly.
                AccountKeyPath = new RootedKeyPath(RootExtKey.GetPublicKey().GetHDFingerPrint(), accountKeyPath);
            }
            public string PartyName;
            public Mnemonic Mnemonic;
            public ExtPubKey AccountExtPubKey;
            public ExtKey RootExtKey;
            public RootedKeyPath AccountKeyPath;
        }

        // We will:
        // 1. Create a multi sig wallet of Alice and Bob
        // 2. Fund it with 1 BTC
        // 3. Send 0.4 BTC to a random address from it
        public static async Task Main(string[] args)
        {
            // Start bitcoind and NBXplorer in regtest:
            // * Run "bitcoind -regtest"
            // * Run ".\build.ps1", then ".\run.ps1 -regtest" in NBXplorer

            var network = Network.RegTest;
            var client = CreateNBXClient(network);

            // Now let's simulate alice and bob in a 2-2 multisig
            var alice = new Party(new Mnemonic(Wordlist.English), "Alice",
                                 new KeyPath("1'/2'/3'"));
            var bob = new Party(new Mnemonic(Wordlist.English), "Bob",
                                new KeyPath("5'/2'/3'"));


            Console.WriteLine($"Alice should secretly save '{alice.Mnemonic}', and remember her password 'Alice'");
            Console.WriteLine("---");
            Console.WriteLine($"Alice should secretly save '{bob.Mnemonic}', and remember her password 'Bob'");

            Console.WriteLine("---");
            Console.WriteLine($"Alice should share '{alice.AccountExtPubKey.GetWif(network)}' with Bob");
            Console.WriteLine("---");
            Console.WriteLine($"Bob should share '{bob.AccountExtPubKey.GetWif(network)}' with Alice");

            var factory = new DerivationStrategyFactory(network);
            var derivationStrategy = factory.CreateMultiSigDerivationStrategy(new[]
            {
                alice.AccountExtPubKey.GetWif(network),
                bob.AccountExtPubKey.GetWif(network)
            }, 2, new DerivationStrategyOptions() { ScriptPubKeyType = ScriptPubKeyType.SegwitP2SH });

            Console.WriteLine("---");
            Console.WriteLine($"The derivation strategy '{derivationStrategy}' represents all the data you need to know to track the multisig wallet");

            // NBXplorer will start tracking this wallet.
            await client.TrackAsync(derivationStrategy);
            // This allow you to get events out of NBXPlorer
            var evts = client.CreateLongPollingNotificationSession();

            // Now let's fund the wallet
            var address1 = (await client.GetUnusedAsync(derivationStrategy, DerivationFeature.Deposit)).Address;

            var rpc = new RPCClient(network);
            // If that fail, your bitcoin node need some bitcoins
            // bitcoin-cli -regtest getnewaddress
            // bitcoin-cli -regtest generatetoaddress 101 <address>
            await rpc.SendToAddressAsync(address1, Money.Coins(1.0m));

            await WaitTransaction(evts, derivationStrategy);
            Console.WriteLine("---");
            Console.WriteLine("Sent some money to the multi sig wallet");
            Console.WriteLine("---");

            // You can list transactions
            var txs = await client.GetTransactionsAsync(derivationStrategy);
            Console.WriteLine($"Number of unconf transactions: {txs.UnconfirmedTransactions.Transactions.Count}");
            Console.WriteLine("---");
            var balance = await client.GetBalanceAsync(derivationStrategy);
            Console.WriteLine($"Balance: {balance.Unconfirmed}");

            Console.WriteLine("---");
            var randomDestination = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
            var psbt = (await client.CreatePSBTAsync(derivationStrategy, new CreatePSBTRequest()
            {
                Destinations =
                {
                    new CreatePSBTDestination()
                    {
                        Destination = randomDestination,
                        Amount = Money.Coins(0.4m),
                        SubstractFees = true // We will pay fee by sending to destination a bit less than 0.4 BTC
                    }
                },
                FeePreference = new FeePreference()
                {
                    // 10 sat/byte. You can remove this in prod, as it will use bitcoin's core estimation.
                    ExplicitFeeRate = new FeeRate(10.0m)
                }
            })).PSBT;

            var signedByAlice = Sign(alice, derivationStrategy, psbt);
            Console.WriteLine("---");
            var signedByBob = Sign  (bob, derivationStrategy, psbt);

            // OK both have signed
            var fullySignedPSBT = signedByAlice.Combine(signedByBob);
            fullySignedPSBT.Finalize();
            var fullySignedTx = fullySignedPSBT.ExtractTransaction();
            await client.BroadcastAsync(fullySignedTx);
            // Let's wait NBX receives the tx
            await WaitTransaction(evts, derivationStrategy);
            balance = await client.GetBalanceAsync(derivationStrategy);
            Console.WriteLine($"New balance: {balance.Unconfirmed}");
        }

        private static PSBT Sign(Party party, DerivationStrategyBase derivationStrategy, PSBT psbt)
        {
            psbt = psbt.Clone();

            // NBXplorer does not have knowledge of the account key path, KeyPath are private information of each peer
            // NBXplorer only derive 0/* and 1/* on top of provided account xpubs,
            // This mean that the input keypaths in the PSBT are in the form 0/* (as if the account key was the root)
            // RebaseKeyPaths modifies the PSBT by adding the AccountKeyPath in prefix of all the keypaths of the PSBT

            // Note that this is not necessary to do this if the account key is the same as root key.
            // Note that also that you don't have to do this, if you do not pass the account key path in the later SignAll call.
            // however, this is best practice to rebase the PSBT before signing.
            // If you sign with an offline device (hw wallet), the wallet would need the rebased PSBT.
            psbt.RebaseKeyPaths(party.AccountExtPubKey, party.AccountKeyPath);

            Console.WriteLine("A PSBT is a data structure with all information for a wallet to sign.");
            var spend = psbt.GetBalance(derivationStrategy, party.AccountExtPubKey, party.AccountKeyPath);
            Console.WriteLine($"{party.PartyName}, Do you agree to sign this transaction spending {spend}?");
            // Ok I sign
            psbt.SignAll(derivationStrategy, // What addresses to derive?
                         party.RootExtKey.Derive(party.AccountKeyPath), // With which account private keys?
                         party.AccountKeyPath); // What is the keypath of the account private key. If you did not rebased the keypath like before, you can remove this parameter
            return psbt;
        }

        static async Task<NewTransactionEvent> WaitTransaction(LongPollingNotificationSession evts, DerivationStrategyBase derivationStrategy)
        {
            while (true)
            {
                var evt = await evts.NextEventAsync();
                if (evt is NBXplorer.Models.NewTransactionEvent tx)
                {
                    if (tx.DerivationStrategy == derivationStrategy)
                        return tx;
                }
            }
        }

        private static ExplorerClient CreateNBXClient(Network network)
        {
            NBXplorerNetworkProvider provider = new NBXplorerNetworkProvider(network.ChainName);
            ExplorerClient client = new NBXplorer.ExplorerClient(provider.GetFromCryptoCode(network.NetworkSet.CryptoCode));
            return client;
        }
    }
}
