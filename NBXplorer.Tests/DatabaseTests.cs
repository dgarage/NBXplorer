using System;
using Dapper;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Npgsql;
using NBitcoin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;
using System.IO;
using System.Diagnostics;
using NBXplorer.Backends.Postgres;
using NBXplorer.Client;

namespace NBXplorer.Tests
{
	public class DatabaseTests
	{
		public DatabaseTests(ITestOutputHelper logs)
		{
			Logs = logs;
		}

		public ITestOutputHelper Logs { get; }

		[Fact]
		[Trait("Benchmark", "Benchmark")]
		public async Task BenchmarkDatabase()
		{
			await using var conn = await GetConnection();
			conn.Execute(GetScript("generate-whale.sql"));
			Logs.WriteLine("Data loaded");
			await Benchmark(conn, "SELECT * FROM wallets_utxos;", 50);
			// Turn block unconf then back to conf
			await Benchmark(conn, "UPDATE blks SET confirmed='f' WHERE code='BTC' AND blk_id='34d73e472c45c8f47e505230f9935a7ff6450e3556285787ffcc935a22e31637';UPDATE blks SET confirmed='t' WHERE code='BTC' AND blk_id='34d73e472c45c8f47e505230f9935a7ff6450e3556285787ffcc935a22e31637';", 50);
			await Benchmark(conn,
				"SELECT ts.script, ts.addr, ts.derivation, ts.keypath, ts.redeem FROM ( VALUES ('BTC', 'blah'), ('BTC', 'blah'), ('BTC', 'blah'), ('BTC', 'blah')) r (code, script), " +
				" LATERAL(" +
				"	SELECT script, addr, descriptor_metadata->>'derivation' derivation, keypath, descriptors_scripts_metadata->>'redeem' redeem, descriptor_metadata->>'blindedAddress' blinded_addr " +
				"	FROM nbxv1_keypath_info ki " +
				"	WHERE ki.code=r.code AND ki.script=r.script) ts;", 50);
			await Benchmark(conn, "SELECT o.tx_id, o.idx, o.value, o.script FROM (VALUES ('BTC', 'hash', 5), ('BTC', 'hash', 5), ('BTC', 'hash', 5))  r (code, tx_id, idx) JOIN outs o USING (code, tx_id, idx);", 50);
			await Benchmark(conn, "SELECT blk_height, tx_id, wu.idx, value, script, nbxv1_get_keypath(d.metadata, ds.idx) AS keypath, d.metadata->>'feature' feature, mempool, input_mempool, seen_at FROM wallets_utxos wu JOIN descriptors_scripts ds USING (code, script) JOIN descriptors d USING (code, descriptor) WHERE code='BTC' AND wallet_id='WHALE' AND immature IS FALSE ", 50);
			await Benchmark(conn, "SELECT * FROM get_wallets_histogram('WHALE', 'BTC', '', '2022-01-01'::timestamptz, '2022-02-01'::timestamptz, interval '1 day');", 50);
			await Benchmark(conn, "SELECT * FROM get_wallets_recent('WHALE', interval '1 week', 100, 0);", 50);
			await Benchmark(conn, "SELECT * FROM descriptors_scripts_unused WHERE code='BTC' AND descriptor='WHALEDESC' ORDER BY idx LIMIT 1 OFFSET 0;", 50);
		}

		private static string GetScript(string script)
		{

			var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
			while (directory != null && !directory.GetFiles("*.csproj").Any())
			{
				directory = directory.Parent;
			}
			return File.ReadAllText(Path.Combine(directory.FullName, "Scripts", script));
		}

		private async Task Benchmark(DbConnection connection, string script, int target)
		{
			bool analyzed = false;
			retry:
			// Warmup
			await connection.ExecuteAsync(script);
			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();
			int iterations = 20;
			await connection.ExecuteAsync(string.Join(';', Enumerable.Range(0, iterations).Select(o => script)));
			stopwatch.Stop();
			var ms = ((int)TimeSpan.FromTicks(stopwatch.ElapsedTicks / iterations).TotalMilliseconds);
			Logs.WriteLine(script + " : " + ms + " ms");
			if (ms >= target)
			{
				if (!analyzed)
				{
					await connection.ExecuteAsync("ANALYZE;");
					goto retry;
				}
				Assert.Fail("Unacceptable response time for " + script);
			}
		}

		[Fact]
		public async Task CanCalculateReplacing()
		{
			await using var conn = await GetConnection();
			async Task AssertBalance(decimal expectedAmount)
			{
				var balance = await conn.QueryFirstAsync<decimal>("SELECT available_balance FROM wallets_balances;");
				Assert.Equal(expectedAmount, balance);
			}
			// Alice has two addresses, fund and address.
			await conn.ExecuteAsync(
				"INSERT INTO wallets VALUES ('Alice');" +
				"INSERT INTO scripts VALUES ('BTC', 'fund', 'fund'), ('BTC', 'a1', 'a1');" +
				"INSERT INTO wallets_scripts (code, wallet_id, script) VALUES ('BTC', 'Alice', 'a1'), ('BTC', 'Alice', 'fund');"
				);

			Assert.True(await conn.ExecuteScalarAsync<bool>("CALL fetch_matches ('BTC', ARRAY[" +
				"('t1', 0, 'fund', 5,'')," +
				"('t1', 1, 'untracked', 10,'')" +
				"]::new_out[]," +
				"ARRAY[]::new_in[], 'f')"));
			await conn.ExecuteAsync("CALL save_matches('BTC');");
			await AssertBalance(5.0m);

			// t2: First spend of untracked
			Assert.True(await conn.ExecuteScalarAsync<bool>("CALL fetch_matches ('BTC', " +
				"ARRAY[" +
				"('t2', 0, 'a1', 1, '')" +
				"]::new_out[]," +
				"ARRAY[" +
				"('t2', 0, 't1', 1)" +
				"]::new_in[], 'f')"));
			await conn.ExecuteAsync("CALL save_matches('BTC');");
			await AssertBalance(5.0m + 1.0m);

			// t2s: Double spend of untracked
			// Note that this double spend doesn't spend any output, or create any output belonging to any wallet.
			// however, this transaction affects wallets, as it double spend t2, a transaction that Alice wallet is
			// interested in.
			Assert.True(await conn.ExecuteScalarAsync<bool>("CALL fetch_matches ('BTC', " +
				"ARRAY[" +
				"" +
				"]::new_out[]," +
				"ARRAY[" +
				"('t2s', 0, 't1', 1)" +
				"]::new_in[], 'f')"));
			var conflict = await conn.QueryFirstAsync("SELECT * FROM matched_conflicts");
			Assert.Equal(conflict.spent_tx_id, "t1");
			Assert.Equal(conflict.spent_idx, 1);
			Assert.Equal(conflict.replacing_tx_id, "t2s");
			Assert.Equal(conflict.replaced_tx_id, "t2");
			await conn.ExecuteAsync("CALL save_matches('BTC');");
			await AssertBalance(5.0m);

			// Another double spent, but this time having an output
			Assert.True(await conn.ExecuteScalarAsync<bool>("CALL fetch_matches ('BTC', " +
				"ARRAY[" +
				"('t2ss', 0, 'a1', 51, '')" +
				"]::new_out[]," +
				"ARRAY[" +
				"('t2ss', 0, 't1', 1)" +
				"]::new_in[], 'f')"));
			conflict = await conn.QueryFirstAsync("SELECT * FROM matched_conflicts");
			Assert.Equal(conflict.spent_tx_id, "t1");
			Assert.Equal(conflict.spent_idx, 1);
			Assert.Equal(conflict.replacing_tx_id, "t2ss");
			Assert.Equal(conflict.replaced_tx_id, "t2s");
			await conn.ExecuteAsync("CALL save_matches('BTC');");
			await AssertBalance(5.0m + 51m);

			// Check t2s is tracked despite not having any input/output.
			foreach (var txid in new[] { "t2s", "t2ss" })
			{
				await conn.QueryFirstAsync($"SELECT * FROM txs WHERE tx_id='{txid}'");
			}
		}


		[Fact]
		public async Task CanCalculateHistogram()
		{
			await using var conn = await GetConnection();
			int txcount = 0;
			int blkcount = 0;
			await conn.ExecuteAsync(
				"INSERT INTO wallets VALUES ('Alice');" +
				"INSERT INTO scripts VALUES ('BTC', 'a1', 'a1');" +
				"INSERT INTO wallets_scripts (code, wallet_id, script) VALUES ('BTC', 'Alice', 'a1');"
				);

			async Task Receive(long value, DateTimeOffset date)
			{
				await conn.ExecuteAsync(
				"INSERT INTO txs (code, tx_id, mempool, seen_at) VALUES ('BTC', @tx, 't', @seen_at);" +
				"INSERT INTO outs VALUES ('BTC', @tx, 0, 'a1', @val);" +
				"INSERT INTO blks VALUES ('BTC', @blk, @blkcount, 'f');" +
				"INSERT INTO blks_txs (code, tx_id, blk_id) VALUES ('BTC', @tx, @blk);" +
				"UPDATE blks SET confirmed='t' WHERE code='BTC' AND blk_id=@blk;",
				new
				{
					tx = "tx" + (txcount++),
					blk = "b" + (blkcount++),
					blkcount = blkcount - 1,
					val = value,
					seen_at = date
				});
			}

			async Task Spend(long value, DateTimeOffset date)
			{
				var utxos = await conn.QueryAsync("SELECT * FROM wallets_utxos WHERE wallet_id='Alice';");
				long total = 0;
				List<(string tx_id, long idx)> spent = new List<(string tx_id, long idx)>();
				foreach (var utxo in utxos)
				{
					while (total < value)
					{
						spent.Add((utxo.tx_id, utxo.idx));
						total += utxo.value;
					}
				}
				Assert.True(total > value, "Not enough money");

				var parameters = new
				{
					tx = "tx" + (txcount++),
					blk = "b" + (blkcount++),
					blkcount = blkcount - 1,
					change = total - value,
					seen_at = date
				};

				// save_matches should take care of inserting the txs automatically. But we want to be in control of the seen_at.
				await conn.ExecuteAsync("INSERT INTO txs (code, tx_id, mempool, seen_at) VALUES ('BTC', @tx, 't', @seen_at);", parameters);

				int i = 0;
				StringBuilder tx_outs = new StringBuilder();
				tx_outs.Append("ARRAY[");
				if (parameters.change != 0)
				{
					if (i != 0)
						tx_outs.Append(',');
					tx_outs.Append($"('{parameters.tx}', 0, 'a1', {parameters.change}, '')");
				}
				tx_outs.Append("]::new_out[]");
				i = 0;
				StringBuilder tx_ins = new StringBuilder();
				tx_ins.Append("ARRAY[");
				foreach (var s in spent)
				{
					if (i != 0)
						tx_ins.Append(',');
					tx_ins.Append($"('{parameters.tx}', {i}, '{s.tx_id}', {s.idx})");
					i++;
				}
				tx_ins.Append("]::new_in[]");
				await conn.ExecuteAsync(
				$"CALL save_matches('BTC', {tx_outs}, {tx_ins});" +
				"INSERT INTO blks VALUES ('BTC', @blk, @blkcount, 'f');" +
				"INSERT INTO blks_txs (code, tx_id, blk_id) VALUES ('BTC', @tx, @blk);" +
				"UPDATE blks SET confirmed='t' WHERE code='BTC' AND blk_id=@blk;",
				parameters);
			}

			var date = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var from = date;
			await Receive(50, date);
			date += TimeSpan.FromMinutes(2);  // 2
			await Spend(20, date);
			date += TimeSpan.FromMinutes(10); // 12
			await Spend(10, date);
			date += TimeSpan.FromMinutes(1); // 13
			await Receive(10, date);
			date += TimeSpan.FromMinutes(8); // 21
			await Receive(30, date);
			date += TimeSpan.FromMinutes(20); // 41
			await Spend(50, date);
			date += TimeSpan.FromMinutes(10); // 51
			await Receive(2, date);
			date += TimeSpan.FromMinutes(2); // 53
			await Spend(1, date);
			Assert.True(conn.ExecuteScalar<bool>("SELECT wallets_history_refresh();"));
			var r1 = await conn.QueryAsync("SELECT * FROM get_wallets_histogram('Alice', 'BTC', '', @from, @to, interval '5 minutes')", new
			{
				from = from,
				to = from + TimeSpan.FromHours(1.0)
			});

			var expected = new (long, long)[]
			{
				(30,30),
				(0,30),
				(0,30),
				(0,30),
				(30,60),
				(0,60),
				(0,60),
				(0,60),
				(-50,10),
				(0,10),
				(1,11),
				(0,11)
			};
			foreach (var pair in Enumerable.Zip(r1.Select(o => ((long)o.balance_change, (long)o.balance)),
						expected))
			{
				Assert.Equal(pair.Second, pair.First);
			}

			r1 = await conn.QueryAsync("SELECT * FROM get_wallets_histogram('Alice', 'BTC', '', @from, @to, interval '5 minutes')", new
			{
				from = from + TimeSpan.FromMinutes(30.0),
				to = from + TimeSpan.FromHours(1.0)
			});

			expected = expected.Skip(6).ToArray();
			foreach (var pair in Enumerable.Zip(r1.Select(o => ((long)o.balance_change, (long)o.balance)),
						expected))
			{
				Assert.Equal(pair.Second, pair.First);
			}
		}

		[Fact]
		public async Task CanDetectDoubleSpending()
		{
			await using var conn = await GetConnection();
			// t0 has an output, then t1 spend it, followed by t2.
			// t1 should be marked replaced_by
			// then t3 spend the input
			// t2 should be marked replaced_by
			await conn.ExecuteAsync(
				"INSERT INTO txs (code, tx_id, mempool) VALUES ('BTC', 't0', 't'), ('BTC', 't1', 't'),  ('BTC', 't2', 't'), ('BTC', 't3', 't'), ('BTC', 't4', 't'), ('BTC', 't5', 't');" +
				"INSERT INTO scripts VALUES ('BTC', 'a1', '');" +
				"CALL save_matches ('BTC', " +
				"ARRAY[('t0', 10, 'a1', 5, '')]::new_out[]," +
				"ARRAY[('t1', 0, 't0', 10)]::new_in[]);" +
				"CALL save_matches ('BTC', " +
				"ARRAY[]::new_out[]," +
				"ARRAY[('t2', 0, 't0', 10)]::new_in[]);"
				);
			var conflict = await conn.QueryFirstAsync("SELECT * FROM matched_conflicts;");
			//Assert.Single(conflict);
			Assert.Equal("t1", conflict.replaced_tx_id);
			Assert.Equal("t2", conflict.replacing_tx_id);
			var t1 = await conn.QueryFirstAsync("SELECT * FROM txs WHERE tx_id='t1'");
			Assert.True(t1.mempool);
			Assert.Equal("t2", t1.replaced_by);

			var t2 = await conn.QueryFirstAsync("SELECT * FROM txs WHERE tx_id='t2'");
			Assert.True(t2.mempool);
			Assert.Null(t2.replaced_by);

			await conn.ExecuteAsync("CALL save_matches ('BTC', ARRAY[]::new_out[], ARRAY[('t3', 0, 't0', 10)]::new_in[]);");
			t2 = await conn.QueryFirstAsync("SELECT * FROM txs WHERE tx_id='t2'");
			Assert.True(t2.mempool);
			Assert.Equal("t3", t2.replaced_by);

			// Does it propagate to other children? t3 get spent by t4 then t3 get double spent by t5.
			// We expect t3 and t4 to be double spent
			await conn.ExecuteAsync("INSERT INTO outs VALUES('BTC', 't3', 10, 'a1', 5);" +
				"CALL save_matches ('BTC', ARRAY[]::new_out[], ARRAY[('t4', 0, 't3', 10)]::new_in[]);" +
				"CALL save_matches ('BTC', ARRAY[]::new_out[], ARRAY[('t5', 0, 't0', 10)]::new_in[]);");

			var t3 = await conn.QueryFirstAsync("SELECT * FROM txs WHERE tx_id='t3'");
			Assert.True(t3.mempool);
			Assert.Equal("t5", t3.replaced_by);

			var t4 = await conn.QueryFirstAsync("SELECT * FROM txs WHERE tx_id='t4'");
			Assert.True(t4.mempool);
			Assert.Equal("t5", t4.replaced_by);

			var t5 = await conn.QueryFirstAsync("SELECT * FROM txs WHERE tx_id='t5'");
			Assert.True(t5.mempool);
			Assert.Null(t5.replaced_by);
		}

		[Fact]
		public async Task CanGetMatches()
		{
			await using var conn = await GetConnection();
			await conn.ExecuteAsync("INSERT INTO scripts VALUES ('BTC', 'a1', '');");
			await conn.ExecuteAsync("CALL fetch_matches ('BTC', ARRAY[" +
				"('t1', 0, 'a1', 5,'')," +
				"('t1', 0, 'a1', 7,'')," + // dup
				"('t1', 1, 'a3', 6,'')," +  // shouldn't be tracked
				"('t1', 1, 'a1', 6,'')" +
				"]::new_out[]," +
				"ARRAY[" +
				"('t2', 0, 't1', 0)," +
				"('t2', 1, 't2', 0)," + // shouldn't be tracked
				"('t2', 0, 't1', 0)" +  // dup
				"]::new_in[])");

			var result = (await conn.QueryAsync("SELECT * FROM matched_outs")).ToList();
			Assert.Equal(2, result.Count);
			Assert.Contains(result, o => o.tx_id == "t1" && o.idx == 0 && o.order == 0);
			Assert.Contains(result, o => o.tx_id == "t1" && o.idx == 1 && o.order == 3);

			result = (await conn.QueryAsync("SELECT * FROM matched_ins")).ToList();
			var row = Assert.Single(result);
			Assert.Equal("t2", row.tx_id);
			Assert.Equal(0, row.idx);
			Assert.Equal(0, row.order);
		}

		[Fact]
		public async Task CanTrackGap()
		{
			await using var conn = await GetConnection();
			await conn.ExecuteAsync(
				"INSERT INTO descriptors VALUES ('BTC', 'd1');" +
				"INSERT INTO scripts VALUES ('BTC', 's1', '');" +
				"INSERT INTO scripts VALUES ('BTC', 's-used', '', 't');");

			async Task AssertGap(int expectedNextIndex, int expectedGap)
			{
				var actual = await conn.QueryFirstAsync<(long next, long gap)>("SELECT next_idx, gap FROM descriptors WHERE descriptor='d1'");
				Assert.Equal(expectedNextIndex, actual.next);
				Assert.Equal(expectedGap, actual.gap);
			}
			await AssertGap(0, 0);
			await conn.ExecuteAsync("INSERT INTO descriptors_scripts VALUES ('BTC', 'd1', 0, 's1');");
			await AssertGap(1, 1);
			await conn.ExecuteAsync("INSERT INTO descriptors_scripts VALUES ('BTC', 'd1', 1, 's1');");
			await AssertGap(2, 2);
			// 2 is used
			await conn.ExecuteAsync("INSERT INTO descriptors_scripts VALUES ('BTC', 'd1', 2, 's-used');");
			Assert.True(conn.ExecuteScalar<bool>("SELECT used FROM descriptors_scripts WHERE idx=2;"));
			await AssertGap(3, 0);
			await conn.ExecuteAsync("INSERT INTO descriptors_scripts VALUES ('BTC', 'd1', 3, 's1');");
			await AssertGap(4, 1);
			// 4 is used
			await conn.ExecuteAsync("INSERT INTO descriptors_scripts VALUES ('BTC', 'd1', 4, 's-used');");
			await AssertGap(5, 0);
			// 4 get unused
			await conn.ExecuteAsync("UPDATE descriptors_scripts SET used='f' WHERE idx=4;");
			await AssertGap(5, 2);
			// 2 get unused
			await conn.ExecuteAsync("UPDATE descriptors_scripts SET used='f' WHERE idx=2;");
			await AssertGap(5, 5);
			// If s1 get used, it should propageate and the gap limit should be 1 since 4 isn't s1.
			await conn.ExecuteAsync("UPDATE scripts SET used='t' WHERE script='s1';");
			await AssertGap(5, 1);
		}

		[Fact]
		public async Task CanUseHelperFunctions()
		{
			await using var conn = await GetConnection();
			var wk1 = DBUtils.nbxv1_get_wallet_id("BTC", "address1");
			var wk2 = DBUtils.nbxv1_get_wallet_id("BTC", "sWra[t1");
			var dk = DBUtils.nbxv1_get_descriptor_id("BTC", "strat1", "Deposit");

			Assert.Equal(wk1, conn.QueryFirst<string>("SELECT nbxv1_get_wallet_id('BTC', 'address1')"));
			Assert.Equal(wk2, conn.QueryFirst<string>("SELECT nbxv1_get_wallet_id('BTC', 'sWra[t1')"));
			Assert.Equal(dk, conn.QueryFirst<string>("SELECT nbxv1_get_descriptor_id('BTC', 'strat1', 'Deposit')"));
		}
		[Fact]
		public async Task CanOutsChangesPropagate()
		{
			await using var conn = await GetConnection();
			await conn.ExecuteAsync(
				"INSERT INTO scripts VALUES ('BTC', 'a1', '');" +
				"INSERT INTO txs VALUES ('BTC', 't1'), ('BTC', 't2');" +
				"INSERT INTO outs VALUES ('BTC', 't1', '3', 'a1', 5, 'ff');" +
				"INSERT INTO ins VALUES ('BTC', 't2', '1', 't1', 3);");

			var r = conn.QueryFirst("SELECT * FROM ins WHERE tx_id='t2'");
			Assert.Equal("ff", r.asset_id);
			Assert.Equal(5, r.value);

			r = conn.QueryFirst("SELECT * FROM ins_outs WHERE tx_id='t2' AND is_out IS FALSE");
			Assert.Equal("ff", r.asset_id);
			Assert.Equal(5, r.value);

			r = conn.QueryFirst("SELECT * FROM ins_outs WHERE tx_id='t1' AND is_out IS TRUE");
			Assert.Equal("ff", r.asset_id);
			Assert.Equal(5, r.value);

			await conn.ExecuteAsync("UPDATE outs SET asset_id='aa', value=6");

			r = conn.QueryFirst("SELECT * FROM ins WHERE tx_id='t2'");
			Assert.Equal("aa", r.asset_id);
			Assert.Equal(6, r.value);

			r = conn.QueryFirst("SELECT * FROM ins_outs WHERE tx_id='t2' AND is_out IS FALSE");
			Assert.Equal("aa", r.asset_id);
			Assert.Equal(6, r.value);

			r = conn.QueryFirst("SELECT * FROM ins_outs WHERE tx_id='t1' AND is_out IS TRUE");
			Assert.Equal("aa", r.asset_id);
			Assert.Equal(6, r.value);
		}


		[Fact]
		public async Task CanCalculateUTXO()
		{
			await using var conn = await GetConnection();
			await conn.ExecuteAsync(
				"INSERT INTO wallets VALUES ('Alice');" +
				"INSERT INTO scripts VALUES ('BTC', 'a1', '');" +
				"INSERT INTO wallets_scripts VALUES ('BTC', 'a1', 'Alice');" +
				"CALL save_matches('BTC', ARRAY[('t1', 10, 'a1', 5, '')]::new_out[], ARRAY[]::new_in[]);");
			Assert.Single(await conn.QueryAsync("SELECT * FROM wallets_utxos WHERE wallet_id='Alice'"));

			await conn.ExecuteAsync(
				"INSERT INTO blks VALUES ('BTC', 'b1', 0, 'b0');" +
				"INSERT INTO blks_txs (code, tx_id, blk_id) VALUES ('BTC', 't1', 'b1');" +
				"UPDATE blks SET confirmed='t' WHERE blk_id='b1';");

			Assert.Equal("b1", conn.ExecuteScalar<string>("SELECT blk_id FROM txs WHERE tx_id='t1'"));

			Assert.Single(await conn.QueryAsync("SELECT * FROM wallets_utxos WHERE wallet_id='Alice'"));

			await conn.ExecuteAsync("UPDATE blks SET confirmed='f' WHERE blk_id='b1';");

			Assert.Null(conn.ExecuteScalar<string>("SELECT blk_id FROM txs WHERE tx_id='t1'"));
			await conn.ExecuteAsync("UPDATE blks SET confirmed='t' WHERE blk_id='b1';");
			Assert.Equal("b1", conn.ExecuteScalar<string>("SELECT blk_id FROM txs WHERE tx_id='t1'"));
			await conn.ExecuteAsync("UPDATE blks SET confirmed='f' WHERE blk_id='b1';");

			var balance = conn.QuerySingle("SELECT * FROM wallets_balances WHERE wallet_id='Alice';");
			Assert.Equal(0, balance.confirmed_balance);

			await conn.ExecuteAsync(
				"INSERT INTO blks VALUES ('BTC', 'b2', 0, 'b0');" +
				"INSERT INTO blks_txs (code, tx_id, blk_id) VALUES ('BTC', 't1', 'b2');" +
				"UPDATE blks SET confirmed='t' WHERE blk_id='b2';");

			balance = conn.QuerySingle("SELECT * FROM wallets_balances WHERE wallet_id='Alice';");
			Assert.Equal(5, balance.confirmed_balance);
			Assert.Equal(5, balance.available_balance);

			await conn.ExecuteAsync(
				"CALL save_matches('BTC', ARRAY[]::new_out[], ARRAY[('t2', 0, 't1', 10)]::new_in[]);");

			balance = conn.QuerySingle("SELECT * FROM wallets_balances WHERE wallet_id='Alice';");
			Assert.Equal(5, balance.confirmed_balance);
			Assert.Equal(0, balance.available_balance);

			await conn.ExecuteAsync("UPDATE txs SET mempool='f' WHERE tx_id='t2'");

			balance = conn.QuerySingle("SELECT * FROM wallets_balances WHERE wallet_id='Alice';");
			Assert.Equal(5, balance.confirmed_balance);
			Assert.Equal(5, balance.available_balance);

			await conn.ExecuteAsync("UPDATE txs SET mempool='t', replaced_by='t1' WHERE tx_id='t2'");
			balance = conn.QuerySingle("SELECT * FROM wallets_balances WHERE wallet_id='Alice';");
			Assert.Equal(5, balance.confirmed_balance);
			Assert.Equal(5, balance.available_balance);

			await conn.ExecuteAsync(
				"INSERT INTO blks VALUES ('BTC', 'b3', 1, 'b2');" +
				"INSERT INTO blks_txs (code, tx_id, blk_id) VALUES ('BTC', 't2', 'b3');" +
				"UPDATE blks SET confirmed='t' WHERE blk_id='b3';");

			balance = conn.QuerySingleOrDefault("SELECT * FROM wallets_balances WHERE wallet_id='Alice';");
			Assert.Null(balance);
			await conn.ExecuteAsync("UPDATE blks SET confirmed='f' WHERE blk_id='b3';");

			balance = conn.QuerySingle("SELECT * FROM wallets_balances WHERE wallet_id='Alice';");
			Assert.Equal(5, balance.confirmed_balance);
			Assert.Equal(0, balance.available_balance);

			// What if we have some shitcoins and some assets?
			await conn.ExecuteAsync(
			"INSERT INTO scripts VALUES ('LTC', 'l1', '');" +
			"INSERT INTO wallets_scripts VALUES ('LTC', 'l1', 'Alice');" +
			"CALL save_matches('LTC', ARRAY[('lt1', 10, 'l1', 8, ''), ('lt1', 0, 'l1', 9, 'ASS')]::new_out[], ARRAY[]::new_in[]);");

			await conn.ExecuteAsync(
			"INSERT INTO blks VALUES ('LTC', 'lb1', 0, 'lb0');" +
			"INSERT INTO blks_txs (code, tx_id, blk_id) VALUES ('LTC', 'lt1', 'lb1');" +
			"UPDATE blks SET confirmed='t' WHERE blk_id='lb1';");

			var balances = conn.Query("SELECT * FROM wallets_balances WHERE wallet_id='Alice';");
			Assert.Contains(balances, b => b.asset_id == "ASS" && b.code == "LTC" && b.confirmed_balance == 9);
			Assert.Contains(balances, b => b.asset_id == "" && b.code == "LTC" && b.confirmed_balance == 8);
			Assert.Contains(balances, b => b.asset_id == "" && b.code == "BTC" && b.confirmed_balance == 5);

			// We spend some of BTC, LTC and asset

			// lt2: We spend the LTC and the ASS. Get 2 LTC of change
			// lt3: We get 1 ASS
			// t3: We spend BTC and receive 3 back
			await conn.ExecuteAsync(
				"CALL save_matches('LTC'," +
				"ARRAY[('lt2', 0, 'l1', 2, '')]::new_out[]," +
				"ARRAY[('lt2', 0, 'lt1', 10), ('lt2', 1, 'lt1', 0)]::new_in[]);" +
				"CALL save_matches('LTC'," +
				"ARRAY[('lt3', 0, 'l1', 1, 'ASS')]::new_out[]," +
				"ARRAY[]::new_in[]);" +
				"CALL save_matches('BTC'," +
				"ARRAY[('t3', 0, 'a1', 3, '')]::new_out[]," +
				"ARRAY[('t3', 0, 't1', 10)]::new_in[]);");

			balances = conn.Query("SELECT * FROM wallets_balances WHERE wallet_id='Alice';");
			Assert.Contains(balances, b => b.asset_id == "ASS" && b.code == "LTC" && b.unconfirmed_balance == 1);
			Assert.Contains(balances, b => b.asset_id == "" && b.code == "LTC" && b.unconfirmed_balance == 2);
			Assert.Contains(balances, b => b.asset_id == "" && b.code == "BTC" && b.unconfirmed_balance == 3);

			await conn.ExecuteAsync(
			"INSERT INTO blks VALUES ('LTC', 'lb2', 1, 'lb1');" +
			"INSERT INTO blks_txs (code, tx_id, blk_id, blk_idx) VALUES ('LTC', 'lt2', 'lb2', 0), ('LTC', 'lt3', 'lb2', 1);" +
			"UPDATE blks SET confirmed='t' WHERE blk_id='lb2';");
			await conn.ExecuteAsync(
			"INSERT INTO blks VALUES ('BTC', 'b4', 2, 'b3');" +
			"INSERT INTO blks_txs (code, tx_id, blk_id) VALUES ('BTC', 't3', 'b4');" +
			"UPDATE blks SET confirmed='t' WHERE blk_id='b4';");

			await conn.ExecuteAsync("SELECT wallets_history_refresh();");
			var expectedHistory = new[]
			{
				("lt2", -6, 2),
				("lt1", 8, 8),
			};
			var rows = await conn.QueryAsync("SELECT * FROM wallets_history WHERE code='LTC' AND asset_id='' ORDER BY seen_at DESC");
			AssertHistory(expectedHistory, rows);
			rows = await conn.QueryAsync("SELECT * FROM get_wallets_recent('Alice', interval '1 week', 100, 0) WHERE code='LTC' AND asset_id='' ORDER BY seen_at DESC;");
			AssertHistory(expectedHistory, rows);

			expectedHistory = new[]
			{
				("lt3", 1, 1),
				("lt2", -9, 0),
				("lt1", 9, 9)
			};
			rows = await conn.QueryAsync("SELECT * FROM wallets_history WHERE code='LTC' AND asset_id='ASS' ORDER BY nth DESC");
			AssertHistory(expectedHistory, rows);
			rows = await conn.QueryAsync("SELECT * FROM get_wallets_recent('Alice', interval '1 week', 100, 0) WHERE code='LTC' AND asset_id='ASS';");
			AssertHistory(expectedHistory, rows);
			expectedHistory = new[]
			{
				("lt2", -9, 0)
			};
			rows = await conn.QueryAsync("SELECT * FROM get_wallets_recent('Alice', 'LTC', 'ASS', interval '1 week', 1, 1);");
			AssertHistory(expectedHistory, rows);

			expectedHistory = new[]
			{
				("t3", -2, 3),
				("t1", 5, 5)
			};
			rows = await conn.QueryAsync("SELECT * FROM wallets_history WHERE code='BTC' AND asset_id='' ORDER BY nth DESC");
			AssertHistory(expectedHistory, rows);
			rows = await conn.QueryAsync("SELECT * FROM get_wallets_recent('Alice', interval '1 week', 100, 0) WHERE code='BTC' AND asset_id='';");
			AssertHistory(expectedHistory, rows);
		}

		private static void AssertHistory((string, int, int)[] expectedHistory, IEnumerable<dynamic> rows)
		{
			int expectedRows = expectedHistory.Length;
			int actualRows;
			if (rows.TryGetNonEnumeratedCount(out actualRows))
				Assert.Equal(expectedRows, actualRows);
			foreach (var t in rows.Zip(expectedHistory, (r, h) =>
						(
						expectedTxId: h.Item1,
						expectedChange: h.Item2,
						expectedTotal: h.Item3,
						actualChange: (int)r.balance_change,
						actualTotal: (int)r.balance_total,
						actualTxId: (string)r.tx_id)))
			{
				Assert.Equal(t.expectedTxId, t.actualTxId);
				Assert.Equal(t.expectedChange, t.actualChange);
				Assert.Equal(t.expectedTotal, t.actualTotal);
			}
		}

		[Fact]
		public async Task CanMempoolPropagate()
		{
			await using var conn = await GetConnection();
			// t1 get spent by t2 then t2 by t3. But then, t4 double spend t2 and get validated.
			// So t2 and t3 should get out of mempool.
			await conn.ExecuteAsync(
				"INSERT INTO scripts VALUES ('BTC', 'script', '');" +
				"CALL save_matches('BTC', ARRAY[('t1', 0, 'script', 5, '')]::new_out[], ARRAY[]::new_in[]);" +
				"CALL save_matches('BTC', ARRAY[('t2', 0, 'script', 5, '')]::new_out[], ARRAY[('t2', 0, 't1', 0)]::new_in[]);" +
				"CALL save_matches('BTC', ARRAY[]::new_out[], ARRAY[('t3', 0, 't2', 0)]::new_in[]);" +
				"CALL save_matches('BTC', ARRAY[]::new_out[], ARRAY[('t4', 0, 't1', 0)]::new_in[]);" + // t4 double spend t2

				"INSERT INTO blks (code, blk_id, height, prev_id) VALUES ('BTC', 'b1', 1, 'b0');" +
				"INSERT INTO blks_txs (code, blk_id, tx_id) VALUES ('BTC', 'b1', 't4'), ('BTC', 'b1', 't1');" +
				"UPDATE blks SET confirmed='t' WHERE blk_id='b1';");

			var t3 = await conn.QueryFirstAsync("SELECT * FROM txs WHERE tx_id='t3'");
			Assert.False(t3.mempool);
			Assert.Null(t3.blk_id);
			Assert.Equal("t4", t3.replaced_by);

			var t2 = await conn.QueryFirstAsync("SELECT * FROM txs WHERE tx_id='t2'");
			Assert.False(t2.mempool);
			Assert.Null(t2.blk_id);
			Assert.Equal("t4", t2.replaced_by);

			var t1 = await conn.QueryFirstAsync("SELECT * FROM txs WHERE tx_id='t1'");
			Assert.False(t1.mempool);
			Assert.Equal("b1", t1.blk_id);

			var t4 = await conn.QueryFirstAsync("SELECT * FROM txs WHERE tx_id='t4'");
			Assert.False(t4.mempool);
			Assert.Equal("b1", t4.blk_id);
		}

		[Fact]
		public async Task CanMempoolPropagate2()
		{
			await using var conn = await GetConnection();
			// t1 get spent by t2 then t2 by t3. But then, t4 double spend t2, but t2 get validated.
			// So t4 should get out of mempool.
			await conn.ExecuteAsync(
				"INSERT INTO scripts VALUES ('BTC', 'script', '');" +
				"CALL save_matches('BTC', ARRAY[('t1', 0, 'script', 5, '')]::new_out[], ARRAY[]::new_in[]);" +
				"CALL save_matches('BTC', ARRAY[('t2', 0, 'script', 5, '')]::new_out[], ARRAY[('t2', 0, 't1', 0)]::new_in[]);" +
				"CALL save_matches('BTC', ARRAY[]::new_out[], ARRAY[('t3', 0, 't2', 0)]::new_in[]);" +
				"CALL save_matches('BTC', ARRAY[]::new_out[], ARRAY[('t4', 0, 't1', 0)]::new_in[]);" + // t4 double spend t2

				"INSERT INTO blks (code, blk_id, height, prev_id) VALUES ('BTC', 'b1', 1, 'b0');" +
				"INSERT INTO blks_txs (code, blk_id, tx_id) VALUES ('BTC', 'b1', 't2'), ('BTC', 'b1', 't1');" + // but at the end, t2 get confirmed
				"UPDATE blks SET confirmed='t' WHERE blk_id='b1';");

			var t3 = await conn.QueryFirstAsync("SELECT * FROM txs WHERE tx_id='t3'");
			Assert.True(t3.mempool);
			Assert.Null(t3.blk_id);
			Assert.Null(t3.replaced_by);

			var t2 = await conn.QueryFirstAsync("SELECT * FROM txs WHERE tx_id='t2'");
			Assert.False(t2.mempool);
			Assert.Equal("b1", t2.blk_id);
			Assert.Null(t2.replaced_by);

			var t1 = await conn.QueryFirstAsync("SELECT * FROM txs WHERE tx_id='t1'");
			Assert.False(t1.mempool);
			Assert.Equal("b1", t1.blk_id);

			var t4 = await conn.QueryFirstAsync("SELECT * FROM txs WHERE tx_id='t4'");
			Assert.False(t4.mempool);
			Assert.Null(t4.blk_id);
			Assert.Equal("t2", t4.replaced_by);
		}
		[Fact]
		public async Task CanAddDeleteDescriptorsFromWallets()
		{
			await using var conn = await GetConnection();
			await conn.ExecuteAsync(
				"INSERT INTO wallets(wallet_id) VALUES ('Alice');" +
				"INSERT INTO scripts(code, script, addr) VALUES" +
				"('BTC', 'alice1', '')," +
				"('BTC', 'alice2', '')," +
				"('BTC', 'alice3', '')," +
				"('BTC', 'alice4', '')," +
				"('BTC', 'bob1', '');" +
				"INSERT INTO descriptors (code, descriptor) VALUES " +
				"('BTC', 'AliceD');" +
				"INSERT INTO descriptors_scripts (code, descriptor, idx, script) VALUES " +
				"('BTC', 'AliceD', 1, 'alice1')," +
				"('BTC', 'AliceD', 2, 'alice2')");

			Assert.Null(await conn.QueryFirstOrDefaultAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Alice'"));

			// Adding the descriptor to alice's wallet should add all the script from the generator to her.
			await conn.ExecuteAsync("INSERT INTO wallets_descriptors (code, descriptor, wallet_id) VALUES ('BTC', 'AliceD', 'Alice')");
			var rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Alice'");
			Assert.Equal(2, rows.Count());
			Assert.Contains(rows, r => r.script == "alice1");
			Assert.Contains(rows, r => r.script == "alice2");

			// A new descriptor script appear, should be added automatically to Alice
			await conn.ExecuteAsync("INSERT INTO descriptors_scripts (code, descriptor, idx, script) VALUES " +
									"('BTC', 'AliceD', 3, 'alice3')");
			rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Alice'");
			Assert.Equal(3, rows.Count());
			Assert.Contains(rows, r => r.script == "alice3");

			// A manually inserted address in Alice's wallet
			await conn.ExecuteAsync("INSERT INTO wallets_scripts (code, script, wallet_id) VALUES " +
									"('BTC', 'alice4', 'Alice')");

			// Remove the descriptor from Alice's wallet.
			await conn.ExecuteAsync("DELETE FROM wallets_descriptors");

			// Alice shouldn't have the descriptor's script in her wallet. But still the manually entered one
			rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Alice'");
			Assert.Single(rows);
			Assert.Contains(rows, r => r.script == "alice4");

			// Put back the descriptors
			await conn.ExecuteAsync("INSERT INTO wallets_descriptors (code, descriptor, wallet_id) VALUES ('BTC', 'AliceD', 'Alice')");
			rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Alice'");
			Assert.Equal(4, rows.Count());

			// Now bob come, and Alice becomes a child of Bob, he has one manually inserted row
			await conn.ExecuteAsync("INSERT INTO wallets(wallet_id) VALUES ('Bob');");
			await conn.ExecuteAsync("INSERT INTO wallets_wallets VALUES ('Alice', 'Bob');");
			await conn.ExecuteAsync("INSERT INTO wallets_scripts (code, script, wallet_id) VALUES " +
									"('BTC', 'bob1', 'Bob')");

			// Bob inherited the scripts from Alice?
			rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Bob'");
			Assert.Equal(5, rows.Count());
			Assert.Single(rows, r => r.script == "bob1");
			rows = rows.Where(r => r.script.StartsWith("alice")).ToArray();
			Assert.Equal(4, rows.Count());

			// What about Baby alice, the child of Alice. She has one script, got added to Alice.
			await conn.ExecuteAsync(
				"INSERT INTO wallets(wallet_id) VALUES ('BabyAlice');" +
				"INSERT INTO scripts VALUES ('BTC', 'babyalice1', '');" +
				"INSERT INTO wallets_scripts (code, script, wallet_id) VALUES ('BTC', 'babyalice1', 'BabyAlice')");
			await conn.ExecuteAsync("INSERT INTO wallets_wallets VALUES ('BabyAlice', 'Alice');");

			// Alice should have Baby Alice script
			rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Alice'");
			Assert.Equal(5, rows.Count());
			Assert.Contains(rows, r => r.script == "babyalice1");
			// Bob as well
			rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Bob'");
			Assert.Equal(6, rows.Count());
			Assert.Contains(rows, r => r.script == "babyalice1");

			// Baby alice get a new script!
			await conn.ExecuteAsync(
				"INSERT INTO scripts VALUES ('BTC', 'babyalice2', '');" +
				"INSERT INTO wallets_scripts (code, script, wallet_id) VALUES ('BTC', 'babyalice2', 'BabyAlice')");
			// Bob got it ?
			rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Bob'");
			Assert.Equal(7, rows.Count());
			Assert.Contains(rows, r => r.script == "babyalice2");
			// Alice?
			rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Alice'");
			Assert.Equal(6, rows.Count());
			Assert.Contains(rows, r => r.script == "babyalice2");

			// Baby alice loses a script
			await conn.ExecuteAsync("DELETE FROM wallets_scripts WHERE wallet_id='BabyAlice' AND script='babyalice2'");

			// Alice and Bob should be back to before
			rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Alice'");
			Assert.Equal(5, rows.Count());
			rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Bob'");
			Assert.Equal(6, rows.Count());

			// Alice lose the descriptor which had 3 addresses, she should still have babyscript1 and the alice4 manually entered
			await conn.QueryAsync("DELETE FROM wallets_descriptors WHERE wallet_id='Alice'");
			rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Alice'");
			Assert.Equal(2, rows.Count());
			rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Bob'");
			Assert.Equal(3, rows.Count());

			// Alice isn't the child of Bob anymore so Bob should be left only with his manually entered address
			await conn.ExecuteAsync("DELETE FROM wallets_wallets WHERE parent_id='Bob' AND wallet_id='Alice'");
			rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Bob'");
			Assert.Single(rows);
			Assert.Single(rows, r => r.script == "bob1");

			// Alice come back! Should be back before removal
			await conn.ExecuteAsync("INSERT INTO wallets_wallets (wallet_id, parent_id) VALUES " +
									"('Alice', 'Bob')");
			rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Alice'");
			Assert.Equal(2, rows.Count());
			rows = await conn.QueryAsync("SELECT * FROM wallets_scripts WHERE wallet_id='Bob'");
			Assert.Equal(3, rows.Count());

			// What happen if Bob is a child of Alice. Cycle should be detected
			var ex = await Assert.ThrowsAsync<PostgresException>
				(() => conn.ExecuteAsync("INSERT INTO wallets_wallets (wallet_id, parent_id) VALUES ('Bob', 'Alice')"));
			Assert.Contains("Cycle detected", ex.Message);

			// Let's see what happen if one wallet has the same wallet_script added by
			// - Manually
			// - Child wallet
			// - Descriptor
			// Only when all those refs are removed, should the wallets_scripts be removed
			void AssertRefCount(int expected)
			{
				Assert.Equal(expected, conn.ExecuteScalar<int>("SELECT ref_count FROM wallets_scripts WHERE wallet_id='t' AND script='alice3'"));
			}

			conn.Execute("INSERT INTO wallets VALUES ('t');");
			conn.Execute("INSERT INTO wallets_scripts VALUES ('BTC', 'alice3', 't');");
			AssertRefCount(1);
			conn.Execute("INSERT INTO wallets_descriptors VALUES ('BTC', 'AliceD', 't');");
			conn.Execute("INSERT INTO wallets_descriptors VALUES ('BTC', 'AliceD', 'Alice');");
			AssertRefCount(2);
			conn.Execute("INSERT INTO wallets_wallets VALUES ('Alice', 't');");
			AssertRefCount(3);

			conn.Execute("DELETE FROM wallets_descriptors WHERE wallet_id='t'");
			AssertRefCount(2);
			conn.Execute("DELETE FROM wallets_descriptors WHERE wallet_id='Alice'");
			AssertRefCount(1);
			conn.Execute("INSERT INTO wallets_descriptors VALUES ('BTC', 'AliceD', 'Alice');");
			AssertRefCount(2);
			conn.Execute("DELETE FROM wallets_wallets WHERE parent_id='t'");
			AssertRefCount(1);
			conn.Execute("UPDATE wallets_scripts SET ref_count=0 WHERE wallet_id='t'");
			Assert.Empty(conn.Query("SELECT * FROM wallets_scripts WHERE wallet_id='t' AND script='alice3'"));
		}

		[Fact]
		public async Task CanCalculateUTXO2()
		{
			await using var conn = await GetConnection();
			await conn.ExecuteAsync(
				"INSERT INTO wallets(wallet_id) VALUES ('Alice'), ('Bob');" +
				"INSERT INTO scripts(code, script, addr) VALUES" +
				"('BTC', 'alice1', '')," +
				"('BTC', 'alice2', '')," +
				"('BTC', 'alice3', '')," +
				"('BTC', 'bob1', '')," +
				"('BTC', 'bob2', '')," +
				"('BTC', 'bob3', '');" +
				"INSERT INTO wallets_scripts (code, wallet_id, script) VALUES " +
				"('BTC', 'Alice', 'alice1')," +
				"('BTC', 'Alice', 'alice2')," +
				"('BTC', 'Alice', 'alice3')," +
				"('BTC', 'Bob', 'bob1')," +
				"('BTC', 'Bob', 'bob2')," +
				"('BTC', 'Bob', 'bob3')");


			// 1 coin to alice, 1 to bob
			await conn.ExecuteAsync(
				"CALL save_matches('BTC', ARRAY[" +
				"('t1', 1, 'alice1', 50, '')," +
				"('t1', 2, 'bob1', 40, '')" +
				"]::new_out[], ARRAY[]::new_in[]);" +
				"INSERT INTO blks VALUES ('BTC', 'b1', 1, 'b0');" +
				"INSERT INTO blks_txs (code, blk_id, tx_id) VALUES ('BTC', 'b1', 't1');");

			// alice spend her coin, get change back, 2 outputs to bob
			await conn.ExecuteAsync(
				"CALL save_matches('BTC', ARRAY[" +
				"('t2', 0, 'bob2', 20, '')," +
				"('t2', 1, 'bob3', 39, '')," +
				"('t2', 2, 'alice2', 1, '')" +
				"]::new_out[], " +
				"ARRAY[" +
				"('t2', 0, 't1', 1)" +
				"]::new_in[]);" +
				"INSERT INTO blks VALUES ('BTC', 'b2', 2, 'b1');" +
				"INSERT INTO blks_txs (code, blk_id, tx_id) VALUES ('BTC', 'b2', 't2');" +
				"UPDATE blks SET confirmed='t' WHERE blk_id=ANY(ARRAY['b1','b2']);");

			await AssertBalance(conn, "b2", "b1");

			// Replayed on different block.
			await conn.ExecuteAsync(
				"INSERT INTO blks VALUES ('BTC', 'b1-2', 1, 'b0'), ('BTC', 'b2-2', 2, 'b1-2');" +
				"INSERT INTO blks_txs (code, blk_id, tx_id) VALUES ('BTC', 'b1-2', 't1'), ('BTC', 'b2-2', 't2');" +
				"UPDATE blks SET confirmed='t' WHERE blk_id=ANY(ARRAY['b1-2', 'b2-2']);");
			await AssertBalance(conn, "b2-2", "b1-2");

			// And again!
			await conn.ExecuteAsync(
				"INSERT INTO blks VALUES ('BTC', 'b1-3', 1, 'b0'), ('BTC', 'b2-3', 2, 'b1-3');" +
				"INSERT INTO blks_txs (code, blk_id, tx_id) VALUES ('BTC', 'b1-3', 't1'), ('BTC', 'b2-3', 't2');" +
				"UPDATE blks SET confirmed='t' WHERE blk_id=ANY(ARRAY['b1-3', 'b2-3']);");
			await AssertBalance(conn, "b2-3", "b1-3");

			// Let's test: If the outputs are double spent, then it should disappear from the wallet balance.
			await conn.ExecuteAsync(
				"CALL save_matches('BTC', ARRAY[]::new_out[], ARRAY[('ds', 0, 't1', 1)]::new_in[]);" + // This one double spend t2
				"INSERT INTO blks VALUES ('BTC', 'bs', 1, 'b0');" +
				"INSERT INTO blks_txs (code, blk_id, tx_id) VALUES ('BTC', 'bs', 'ds');" +
				"UPDATE blks SET confirmed='t' WHERE blk_id='bs';");

			// Alice should have her t1 output spent by the confirmed bs, so she has nothing left
			var balance = await conn.QueryFirstOrDefaultAsync("SELECT * FROM wallets_balances WHERE wallet_id='Alice';");
			Assert.Null(balance);

			// Bob should have t1 unconfirmed
			balance = await conn.QueryFirstOrDefaultAsync("SELECT * FROM wallets_balances WHERE wallet_id='Bob';");
			Assert.Equal(40, balance.unconfirmed_balance);
			Assert.Equal(0, balance.confirmed_balance);
			Assert.Equal(40, balance.available_balance);

			Assert.Single(await conn.QueryAsync("SELECT * FROM txs WHERE tx_id='t2' AND mempool IS FALSE AND replaced_by='ds';"));
		}

		private static async Task AssertBalance(DbConnection conn, string b2, string b1)
		{
			// This will check that there is 4 utxo in total
			// 3 for bobs, 1 for alice, then check what happen after
			// orphaning b2 and b1
			Assert.Single(await conn.QueryAsync("SELECT * FROM wallets_utxos WHERE wallet_id='Alice' AND input_tx_id IS NULL;"));
			var utxos = (await conn.QueryAsync("SELECT * FROM wallets_utxos WHERE wallet_id='Bob' AND input_tx_id IS NULL;")).ToList();
			Assert.Equal(3, utxos.Count);

			var balance = await conn.QueryFirstOrDefaultAsync("SELECT * FROM wallets_balances WHERE wallet_id='Alice';");
			Assert.Equal(1, balance.unconfirmed_balance);
			Assert.Equal(1, balance.confirmed_balance);
			Assert.Equal(1, balance.available_balance);

			balance = await conn.QueryFirstOrDefaultAsync("SELECT * FROM wallets_balances WHERE wallet_id='Bob';");
			Assert.Equal(40 + 20 + 39, balance.unconfirmed_balance);
			Assert.Equal(40 + 20 + 39, balance.confirmed_balance);
			Assert.Equal(40 + 20 + 39, balance.available_balance);

			await conn.ExecuteAsync($"UPDATE blks SET confirmed='f' WHERE blk_id='{b2}';");

			balance = await conn.QueryFirstOrDefaultAsync("SELECT * FROM wallets_balances WHERE wallet_id='Alice';");
			Assert.Equal(1, balance.unconfirmed_balance);
			Assert.Equal(50, balance.confirmed_balance);
			Assert.Equal(1, balance.available_balance);

			balance = await conn.QueryFirstOrDefaultAsync("SELECT * FROM wallets_balances WHERE wallet_id='Bob';");
			Assert.Equal(40 + 20 + 39, balance.unconfirmed_balance);
			Assert.Equal(40, balance.confirmed_balance);
			Assert.Equal(40 + 20 + 39, balance.available_balance);

			Assert.Single(await conn.QueryAsync("SELECT * FROM wallets_utxos WHERE wallet_id='Alice' AND input_mempool IS FALSE AND immature IS FALSE;"));

			await conn.ExecuteAsync($"UPDATE blks SET confirmed='f' WHERE blk_id='{b1}';");

			balance = await conn.QueryFirstOrDefaultAsync("SELECT * FROM wallets_balances WHERE wallet_id='Alice';");
			Assert.Equal(1, balance.unconfirmed_balance);
			Assert.Equal(0, balance.confirmed_balance);
			Assert.Equal(1, balance.available_balance);

			balance = await conn.QueryFirstOrDefaultAsync("SELECT * FROM wallets_balances WHERE wallet_id='Bob';");
			Assert.Equal(40 + 20 + 39, balance.unconfirmed_balance);
			Assert.Equal(0, balance.confirmed_balance);
			Assert.Equal(40 + 20 + 39, balance.available_balance);

			Assert.Empty(await conn.QueryAsync("SELECT * FROM wallets_utxos WHERE wallet_id='Alice' AND mempool IS FALSE;"));
		}

		[Fact]
		public async Task CanRunMigrateTwice()
		{
			var db = $"CanRunMigrateTwice_{RandomUtils.GetUInt32()}";
			await using (var conn = await GetConnection(db))
			{
			}
			await using (var conn = await GetConnection(db))
			{
			}
		}

		private async Task<DbConnection> GetConnection(string dbName = null, [CallerMemberName] string applicationName = null)
		{
			var connectionString = ServerTester.GetTestPostgres(dbName, applicationName);
			Npgsql.NpgsqlConnectionStringBuilder builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
			builder.Pooling = false;
			connectionString = builder.ToString();
			var conf = new ConfigurationBuilder().AddInMemoryCollection(new[] { new KeyValuePair<string, string>("POSTGRES", connectionString) }).Build();
			var container = new ServiceCollection();
			container.AddSingleton<IConfiguration>(conf);
			container.AddLogging(builder =>
			{
				builder.AddFilter("System.Net.Http.HttpClient", LogLevel.Error);
				builder.AddProvider(new XUnitLoggerProvider(Logs));
			});
			NBXplorer.Logging.Logs.Configure(container.BuildServiceProvider().GetRequiredService<ILoggerFactory>());
			new Startup(conf).ConfigureServices(container);
			var provider = container.BuildServiceProvider();
			foreach (var service in provider.GetServices<IHostedService>())
				if (service is HostedServices.DatabaseSetupHostedService)
					await service.StartAsync(default);
			var facto = provider.GetRequiredService<DbConnectionFactory>();
			return await facto.CreateConnection();
		}
	}
}
