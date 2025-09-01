using Dapper;
using NBitcoin;
using NBXplorer.Backend;
using NBXplorer.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace NBXplorer.Tests
{
	public partial class UnitTest1
	{
		[Fact]
		public async Task CanCRUDGroups()
		{
			using var tester = ServerTester.Create();
			var g1 = await tester.Client.CreateGroupAsync();
			void AssertG1Empty()
			{
				Assert.NotNull(g1.GroupId);
				Assert.NotNull(g1.TrackedSource);
				Assert.Equal($"GROUP:{g1.GroupId}", g1.TrackedSource);
				Assert.Empty(g1.Children);
			}
			AssertG1Empty();
			g1 = await tester.Client.GetGroupAsync(g1.GroupId);
			AssertG1Empty();
			Assert.Null(await tester.Client.GetGroupAsync("lol"));
			Assert.Null(await tester.Client.AddGroupChildrenAsync("lol", Array.Empty<GroupChild>()));

			await AssertNBXplorerException(409, tester.Client.AddGroupChildrenAsync(g1.GroupId, [g1.AsGroupChild()]));
			Assert.Null(await tester.Client.AddGroupChildrenAsync(g1.GroupId, [new GroupChild() { TrackedSource = "GROUP:Test" }]));
			Assert.Null(await tester.Client.AddGroupChildrenAsync("Test", [new GroupChild() { TrackedSource = g1.TrackedSource }]));

			var g2 = await tester.Client.CreateGroupAsync();
			g1 = await tester.Client.AddGroupChildrenAsync(g1.GroupId, [g2.AsGroupChild()]);
			Assert.NotNull(g1);
			// Nothing happen if twice
			g1 = await tester.Client.AddGroupChildrenAsync(g1.GroupId, [g2.AsGroupChild()]);
			Assert.Equal(g2.TrackedSource, Assert.Single(g1.Children).TrackedSource);
			await AssertNBXplorerException(409, tester.Client.AddGroupChildrenAsync(g2.GroupId, [g1.AsGroupChild()]));
			g1 = await tester.Client.RemoveGroupChildrenAsync(g1.GroupId, [g2.AsGroupChild()]);
			AssertG1Empty();

			var g3 = await tester.Client.CreateGroupAsync();
			g1 = await tester.Client.AddGroupChildrenAsync(g1.GroupId, [g2.AsGroupChild(), g3.AsGroupChild()]);
			Assert.Equal(2, g1.Children.Length);

			// Adding address in g2 should add the addresse to g1 but not g3
			var addresses = Enumerable.Range(0,10).Select(_ => new Key().GetAddress(ScriptPubKeyType.Legacy, tester.Network).ToString()).ToArray();
			await tester.Client.AddGroupAddressAsync("BTC", g2.GroupId, addresses);
			// Idempotent
			await tester.Client.AddGroupAddressAsync("BTC", g2.GroupId, addresses);

			async Task AssertAddresses(GroupInformation g)
			{
				var groupAddresses = await GetGroupAddressesAsync(tester, "BTC", g.GroupId);
				Assert.Equal(groupAddresses.Length, addresses.Length);
				foreach (var a in addresses)
				{
					Assert.Contains(a, groupAddresses);
				}
			}
			await AssertAddresses(g1);
			await AssertAddresses(g2);
			var g3Addrs = await GetGroupAddressesAsync(tester, "BTC", g3.GroupId);
			Assert.Empty(g3Addrs);

			// Removing g2 should remove all its addresses
			g1 = await tester.Client.RemoveGroupChildrenAsync(g1.GroupId, [g2.AsGroupChild()]);
			await AssertAddresses(g2);
			var g1Addrs = await GetGroupAddressesAsync(tester, "BTC", g1.GroupId);
			Assert.Empty(g1Addrs);

			await AssertNBXplorerException(400, tester.Client.AddGroupChildrenAsync(g2.GroupId, [new GroupChild() { TrackedSource= "DERIVATIONSCHEME:tpubDC45vUDsFAAqwYKz5hSLi5yJLNduJzpmTw6QTMRPrwdXURoyL81H8oZAaL8EiwEgg92qgMa9h1bB4Y1BZpy9CTNPfjfxvFcWxeiKBHCqSdc" }]));
			await AssertNBXplorerException(400, tester.Client.AddGroupChildrenAsync(g2.GroupId, [new GroupChild() { CryptoCode="BTC", TrackedSource = "DERIVATIONSCHEME:lol" }]));
		}

		private async Task<string[]> GetGroupAddressesAsync(ServerTester tester, string code, string groupId)
		{
			await using var conn = await tester.GetService<DbConnectionFactory>().CreateConnection();
			return (await conn.QueryAsync<string>("SELECT s.addr FROM wallets_scripts JOIN scripts s USING (code, script) WHERE code=@code AND wallet_id=@wid", new
			{
				code = code,
				wid = Repository.GetWalletKey(new GroupTrackedSource(groupId)).wid
			})).ToArray();
		}

		[Fact]
		public async Task CanAliceAndBobShareWallet()
		{
			using var tester = ServerTester.Create();
			var bobW = tester.Client.GenerateWallet(new GenerateWalletRequest() { ScriptPubKeyType = ScriptPubKeyType.Segwit });
			var aliceW = tester.Client.GenerateWallet(new GenerateWalletRequest() { ScriptPubKeyType = ScriptPubKeyType.Segwit });

			var shared = await tester.Client.CreateGroupAsync();
			await tester.Client.AddGroupChildrenAsync(shared.GroupId, new[] { bobW, aliceW }.Select(w => new GroupChild() { CryptoCode = "BTC", TrackedSource = w.TrackedSource }).ToArray());

			var unused = tester.Client.GetUnused(bobW.DerivationScheme, DerivationStrategy.DerivationFeature.Deposit);
			var txid = tester.SendToAddress(unused.Address, Money.Coins(1.0m));
			var gts = GroupTrackedSource.Parse(shared.TrackedSource);
			tester.Notifications.WaitForTransaction(gts, txid);

			var balance = await tester.Client.GetBalanceAsync(gts);
			Assert.Equal(Money.Coins(1.0m), balance.Unconfirmed);

			var txs = await tester.Client.GetTransactionsAsync(gts);
			var tx = Assert.Single(txs.UnconfirmedTransactions.Transactions);
			Assert.Equal(txid, tx.TransactionId);
			Assert.NotNull(tx.Outputs[0].Address);

			// Can we track manually added address?
			await tester.Client.AddGroupAddressAsync("BTC", shared.GroupId, ["n3XyBWEKWLxm5EzrrvLCJyCQrRhVWQ8YGa"]);
			txid = tester.SendToAddress(BitcoinAddress.Create("n3XyBWEKWLxm5EzrrvLCJyCQrRhVWQ8YGa", tester.Network), Money.Coins(1.2m));
			var txEvt = tester.Notifications.WaitForTransaction(gts, txid);
			Assert.Single(txEvt.Outputs);
			Assert.NotNull(tx.Outputs[0].Address);

			balance = await tester.Client.GetBalanceAsync(gts);
			Assert.Equal(Money.Coins(1.0m + 1.2m), balance.Unconfirmed);
		}
		
		[Fact]
		public async Task CanGetGroupAddresses()
		{
			using var tester = ServerTester.Create();
			var g = await tester.Client.CreateGroupAsync();
			var addresses = Enumerable.Range(0, 10).Select(_ => new Key().GetAddress(ScriptPubKeyType.TaprootBIP86, tester.Network).ToString()).ToArray();
			await tester.Client.AddGroupAddressAsync("BTC", g.GroupId, addresses);

			var groupAddresses = await tester.Client.GetAddresses(new GroupTrackedSource(g.GroupId));
			Assert.Equal(addresses.Length, groupAddresses.Length);
			foreach (var a in addresses)
			{
				Assert.Contains(BitcoinAddress.Create(a, tester.Network), groupAddresses);
			}
		}
	

		[Fact]
		public async Task CanScanUTXOSetForGroups()
		{
			using var tester = ServerTester.Create();
			var g = await tester.Client.CreateGroupAsync(Cancel);
			var newAddress = new Key().GetAddress(ScriptPubKeyType.TaprootBIP86, tester.Network);
			await tester.Client.AddGroupAddressAsync("BTC", g.GroupId, [newAddress.ToString()], Cancel);

			var txid = await tester.SendToAddressAsync(newAddress, Money.Coins(1.0m));
			tester.RPC.Generate(1);
			var block = await tester.RPC.GetBlockAsync(await tester.RPC.GetBestBlockHashAsync(Cancel), Cancel);
			var match = block.Transactions.Single(t => t.GetHash() == txid);
			var coin = match.Outputs.AsCoins().Single(c => c.ScriptPubKey == newAddress.ScriptPubKey);

			var gts = new GroupTrackedSource(g.GroupId);
			await tester.Client.ScanUTXOSetAsync(gts, cancellation: Cancel);

			ScanUTXOInformation progress = null;
			while (true)
			{
				progress = await tester.Client.GetScanUTXOSetInformationAsync(gts, Cancel);
				Assert.NotNull(progress);
				if (progress.Status is ScanUTXOStatus.Complete or ScanUTXOStatus.Error)
					break;
				await Task.Delay(100, Cancel);
			}
			Assert.Equal(ScanUTXOStatus.Complete, progress.Status);
			Assert.Equal(1, progress.Progress.Found);

			var utxos = await tester.Client.GetUTXOsAsync(gts, Cancel);
			var unspent = utxos.GetUnspentCoins();
			Assert.Single(unspent);
			Assert.Equal(coin.Outpoint, unspent[0].Outpoint);

			var balance = await tester.Client.GetBalanceAsync(gts, Cancel);
			Assert.Equal(Money.Coins(1.0m), balance.Confirmed);
		}

		private async Task<NBXplorerException> AssertNBXplorerException(int httpCode, Task<GroupInformation> task)
		{
			var ex = await Assert.ThrowsAsync<NBXplorerException>(() => task);
			Assert.Equal(httpCode, ex.Error.HttpCode);
			return ex;
		}
	}
}
