using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.Altcoins.HashX11;
using NBitcoin.DataEncoders;
using NBitcoin.RPC;
using NBXplorer.Backends.Postgres;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NBXplorer.Controllers
{
	[PostgresImplementationActionConstraint(true)]
	[Route($"v1/{CommonRoutes.DerivationEndpoint}")]
	[Route($"v1/{CommonRoutes.AddressEndpoint}")]
	[Route($"v1/{CommonRoutes.WalletEndpoint}")]
	[Route($"v1/{CommonRoutes.TrackedSourceEndpoint}")]
	[Authorize]
	public class PostgresMainController : Controller, IUTXOService
	{
		public PostgresMainController(
			DbConnectionFactory connectionFactory,
			KeyPathTemplates keyPathTemplates)
		{
			ConnectionFactory = connectionFactory;
			KeyPathTemplates = keyPathTemplates;
		}

		public DbConnectionFactory ConnectionFactory { get; }
		public KeyPathTemplates KeyPathTemplates { get; }

		[HttpGet("balance")]
		public async Task<IActionResult> GetBalance(TrackedSourceContext trackedSourceContext)
		{
			var trackedSource = trackedSourceContext.TrackedSource;
			var network = trackedSourceContext.Network;
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			await using var conn = await ConnectionFactory.CreateConnection();
			var b = await conn.QueryAsync("SELECT * FROM wallets_balances WHERE code=@code AND wallet_id=@walletId", new { code = network.CryptoCode, walletId = repo.GetWalletKey(trackedSource).wid });
			MoneyBag
				available = new MoneyBag(),
				confirmed = new MoneyBag(),
				immature = new MoneyBag(),
				total = new MoneyBag(),
				unconfirmed = new MoneyBag();
			foreach (var r in b)
			{
				if (r.asset_id == string.Empty)
				{
					confirmed += Money.Satoshis((long)r.confirmed_balance);
					unconfirmed += Money.Satoshis((long)r.unconfirmed_balance - (long)r.confirmed_balance);
					available += Money.Satoshis((long)r.available_balance);
					total += Money.Satoshis((long)r.available_balance + (long)r.immature_balance);
					immature += Money.Satoshis((long)r.immature_balance);
				}
				else
				{
					var assetId = uint256.Parse(r.asset_id);
					confirmed += new AssetMoney(assetId, (long)r.confirmed_balance);
					unconfirmed += new AssetMoney(assetId, (long)r.unconfirmed_balance - (long)r.confirmed_balance);
					available += new AssetMoney(assetId, (long)r.available_balance);
					total += new AssetMoney(assetId, (long)r.available_balance + (long)r.immature_balance);
					immature += new AssetMoney(assetId, (long)r.immature_balance);
				}
			}

			var balance = new GetBalanceResponse()
			{
				Confirmed = Format(network, confirmed),
				Unconfirmed = Format(network, unconfirmed),
				Available = Format(network, available),
				Total = Format(network, total),
				Immature = Format(network, immature)
			};
			balance.Total = balance.Confirmed.Add(balance.Unconfirmed);
			return Json(balance, network.JsonSerializerSettings);
		}

		[HttpPost("associate")]
		[PostgresImplementationActionConstraint(true)]
		public async Task<IActionResult> AssociateScripts(TrackedSourceContext trackedSourceContext, [FromBody] JArray rawRequest)
		{
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			var jsonSerializer = JsonSerializer.Create(trackedSourceContext.Network.JsonSerializerSettings);
			var requests = rawRequest.ToObject<AssociateScriptRequest[]>(jsonSerializer);
			await repo.AssociateScriptsToWalletExplicitly(trackedSourceContext.TrackedSource, requests);
			return Ok();
		}

		[HttpPost("import-utxos")]
		[TrackedSourceContext.TrackedSourceContextRequirement(true)]
		public async Task<IActionResult> ImportUTXOs(TrackedSourceContext trackedSourceContext, [FromBody] JObject rawRequest)
		{
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			var jsonSerializer = JsonSerializer.Create(trackedSourceContext.Network.JsonSerializerSettings);
			var request = rawRequest.ToObject<ImportUTXORequest>(jsonSerializer);

			if (request.Utxos?.Any() is not true)
				return Ok();

			var rpc = trackedSourceContext.RpcClient;

			var coinToTxOut = await rpc.GetTxOuts(request.Utxos);
			var bestBlocks = await rpc.GetBlockHeadersAsync(coinToTxOut.Select(c => c.Value.BestBlock).ToHashSet().ToList());
			var coinsWithHeights = coinToTxOut
				.Select(c => new
				{
					BestBlock = bestBlocks.ByHashes.TryGet(c.Value.BestBlock),
					Outpoint = c.Key,
					RPCTxOut = c.Value
				})
				.Where(c => c.BestBlock is not null)
				.Select(c => new
				{
					Height = c.BestBlock.Height - c.RPCTxOut.Confirmations + 1,
					c.Outpoint,
					c.RPCTxOut
				})
				.ToList();
			var blockHeaders = await rpc.GetBlockHeadersAsync(coinsWithHeights.Where(c => c.RPCTxOut.Confirmations != 0).Select(c => c.Height).Distinct().ToList());

			var scripts = coinToTxOut
				.Select(pair => new AssociateScriptRequest()
				{
					ScriptPubKey = pair.Value.TxOut.ScriptPubKey
				})
				.ToArray();

			var now = DateTimeOffset.UtcNow;
			var trackedTransactions =
				coinsWithHeights
				.Select(c => new
				{
					Block = blockHeaders.ByHeight.TryGet(c.Height),
					c.Height,
					c.RPCTxOut,
					c.Outpoint
				})
				.Where(c => c.Block is not null || c.RPCTxOut.Confirmations == 0)
				.GroupBy(c => c.Outpoint.Hash)
				.Select(g =>
				{
					var coins = g.Select(c => new Coin(c.Outpoint, c.RPCTxOut.TxOut)).ToArray();
					var txInfo = g.First().RPCTxOut;
					var block = g.First().Block;
					var ttx = repo.CreateTrackedTransaction(trackedSourceContext.TrackedSource,
						new TrackedTransactionKey(g.Key, block?.Hash, true) { }, coins, null);
					ttx.Inserted = now;
					ttx.Immature = txInfo.IsCoinBase && txInfo.Confirmations <= repo.Network.NBitcoinNetwork.Consensus.CoinbaseMaturity;
					ttx.FirstSeen = block?.Time ?? now;
					return ttx;
				}).ToArray();

			await repo.AssociateScriptsToWalletExplicitly(trackedSourceContext.TrackedSource, scripts);
			await repo.SaveBlocks(blockHeaders.Select(b => b.ToSlimChainedBlock()).ToList());
			await repo.SaveMatches(trackedTransactions);

			return Ok();
		}

		private IMoney Format(NBXplorerNetwork network, MoneyBag bag)
		{
			if (network.IsElement)
				return RemoveZeros(bag);
			var c = bag.Count();
			if (c == 0)
				return Money.Zero;
			if (c == 1 && bag.First() is Money m)
				return m;
			return RemoveZeros(bag);
		}

		private static MoneyBag RemoveZeros(MoneyBag bag)
		{
			// Super hack to know if we deal with zero
			return new MoneyBag(bag.Where(a => !a.Negate().Equals(a)).ToArray());
		}


		[HttpGet("utxos")]
		public async Task<IActionResult> GetUTXOs(TrackedSourceContext trackedSourceContext)
		{
			var trackedSource = trackedSourceContext.TrackedSource;
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			var network = trackedSourceContext.Network;
			await using var conn = await ConnectionFactory.CreateConnection();
			var height = await conn.ExecuteScalarAsync<long>("SELECT height FROM get_tip(@code)", new { code = network.CryptoCode });
			// On elements, we can't get blinded address from the scriptPubKey, so we need to fetch it rather than compute it
			string addrColumns = "NULL as address";
			var derivationScheme = (trackedSource as DerivationSchemeTrackedSource)?.DerivationStrategy;
			if (network.IsElement && derivationScheme?.Unblinded() is true)
			{
				addrColumns = "ds.metadata->>'blindedAddress' as address";
			}

			string descriptorJoin = string.Empty;
			string descriptorColumns = "NULL as redeem, NULL as keypath, NULL as feature";
			if (derivationScheme is not null)
			{
				descriptorJoin = " JOIN descriptors_scripts ds USING (code, script) JOIN descriptors d USING (code, descriptor)";
				descriptorColumns = "ds.metadata->>'redeem' redeem, nbxv1_get_keypath(d.metadata, ds.idx) AS keypath, d.metadata->>'feature' feature";
			}

			var utxos = (await conn.QueryAsync<(
				long? blk_height,
				string tx_id,
				int idx,
				long value,
				string script,
				string address,
				string redeem,
				string keypath,
				string feature,
				bool mempool,
				bool input_mempool,
				DateTime tx_seen_at)>(
				$"SELECT blk_height, tx_id, wu.idx, value, script, {addrColumns}, {descriptorColumns}, mempool, input_mempool, seen_at " +
				$"FROM wallets_utxos wu{descriptorJoin} WHERE code=@code AND wallet_id=@walletId AND immature IS FALSE", new { code = network.CryptoCode, walletId = repo.GetWalletKey(trackedSource).wid }));
			UTXOChanges changes = new UTXOChanges()
			{
				CurrentHeight = (int)height,
				TrackedSource = trackedSource,
				DerivationStrategy = derivationScheme
			};
			foreach (var utxo in utxos.OrderBy(u => u.tx_seen_at))
			{
				var u = new UTXO()
				{
					Index = utxo.idx,
					Timestamp = new DateTimeOffset(utxo.tx_seen_at),
					Value = Money.Satoshis(utxo.value),
					ScriptPubKey = Script.FromHex(utxo.script),
					Redeem = utxo.redeem is null ? null : Script.FromHex(utxo.redeem),
					TransactionHash = uint256.Parse(utxo.tx_id)
				};
				u.Outpoint = new OutPoint(u.TransactionHash, u.Index);
				if (utxo.blk_height is long)
				{
					u.Confirmations = (int)(height - utxo.blk_height + 1);
				}

				if (utxo.keypath is not null)
				{
					u.KeyPath = KeyPath.Parse(utxo.keypath);
					u.Feature = Enum.Parse<DerivationFeature>(utxo.feature);
				}
				u.Address = utxo.address is null ? u.ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork) : BitcoinAddress.Create(utxo.address, network.NBitcoinNetwork);
				if (!utxo.mempool)
				{
					changes.Confirmed.UTXOs.Add(u);
					if (utxo.input_mempool)
						changes.Unconfirmed.SpentOutpoints.Add(u.Outpoint);
				}
				else if (!utxo.input_mempool)
					changes.Unconfirmed.UTXOs.Add(u);
				else // (utxo.mempool && utxo.input_mempool)
					changes.SpentUnconfirmed.Add(u);
			}
			return Json(changes, network.JsonSerializerSettings);
		}

		[HttpGet("children")]
		public async Task<IActionResult> GetWalletChildren(TrackedSourceContext trackedSourceContext)
		{
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			await using var conn = await ConnectionFactory.CreateConnection();
			var children = await conn.QueryAsync($"SELECT w.wallet_id, w.metadata FROM wallets_wallets ww JOIN wallets w ON ww.wallet_id = w.wallet_id WHERE ww.parent_id=@walletId", new { walletId = repo.GetWalletKey(trackedSourceContext.TrackedSource).wid });

			return Json(children.Select(c => repo.GetTrackedSource(new PostgresRepository.WalletKey(c.wallet_id, c.metadata))).ToArray(), trackedSourceContext.Network.JsonSerializerSettings);
		}
		[HttpGet("parents")]
		public async Task<IActionResult> GetWalletParents(TrackedSourceContext trackedSourceContext)
		{
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			await using var conn = await ConnectionFactory.CreateConnection();
			var children = await conn.QueryAsync($"SELECT w.wallet_id, w.metadata FROM wallets_wallets ww JOIN wallets w ON ww.parent_id = w.wallet_id WHERE ww.wallet_id=@walletId", new { walletId = repo.GetWalletKey(trackedSourceContext.TrackedSource).wid });

			return Json(children.Select(c => repo.GetTrackedSource(new PostgresRepository.WalletKey(c.wallet_id, c.metadata))).ToArray(), trackedSourceContext.Network.JsonSerializerSettings);
		}
		[HttpPost("children")]
		public async Task<IActionResult> AddWalletChild(TrackedSourceContext trackedSourceContext, [FromBody] JObject request)
		{
			var trackedSource = trackedSourceContext.Network.ParseJObject<TrackedSourceRequest>(request).TrackedSource;
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			await repo.EnsureWalletCreated(trackedSource, trackedSourceContext.TrackedSource);
			return Ok();
		}
		[HttpPost("parents")]
		public async Task<IActionResult> AddWalletParent(TrackedSourceContext trackedSourceContext, [FromBody] JObject request)
		{
			var trackedSource = trackedSourceContext.Network.ParseJObject<TrackedSourceRequest>(request).TrackedSource;
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			await repo.EnsureWalletCreated(trackedSourceContext.TrackedSource, trackedSource);
			return Ok();
		}
		[HttpDelete("children")]
		public async Task<IActionResult> RemoveWalletChild(TrackedSourceContext trackedSourceContext, [FromBody] JObject request)
		{
			var repo = (PostgresRepository)trackedSourceContext.Repository;

			var trackedSource = repo.GetWalletKey(trackedSourceContext.Network
				.ParseJObject<TrackedSourceRequest>(request).TrackedSource);
			var conn = await ConnectionFactory.CreateConnection();
			await conn.ExecuteAsync($"DELETE FROM wallets_wallets WHERE wallet_id=@walletId AND parent_id=@parentId", new { walletId = trackedSource.wid, parentId = repo.GetWalletKey(trackedSourceContext.TrackedSource).wid });
			return Ok();
		}
		[HttpDelete("parents")]
		public async Task<IActionResult> RemoveWalletParent(TrackedSourceContext trackedSourceContext, [FromBody] JObject request)
		{
			var repo = (PostgresRepository)trackedSourceContext.Repository;

			var trackedSource = repo.GetWalletKey(trackedSourceContext.Network
				.ParseJObject<TrackedSourceRequest>(request).TrackedSource);
			var conn = await ConnectionFactory.CreateConnection();
			await conn.ExecuteAsync($"DELETE FROM wallets_wallets WHERE wallet_id=@walletId AND parent_id=@parentId", new { walletId = repo.GetWalletKey(trackedSourceContext.TrackedSource).wid, parentId = trackedSource.wid });
			return Ok();
		}
	}
}
