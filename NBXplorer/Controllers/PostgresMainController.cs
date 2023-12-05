using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
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
	public class PostgresMainController :Controller, IUTXOService
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
		public async Task<IActionResult> GetBalance( TrackedSourceContext trackedSourceContext)
		{
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			await using var conn = await ConnectionFactory.CreateConnection();
			var b = await conn.QueryAsync("SELECT * FROM wallets_balances WHERE code=@code AND wallet_id=@walletId", new { code = trackedSourceContext.Network.CryptoCode, walletId = repo.GetWalletKey(trackedSourceContext.TrackedSource).wid });
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
				Confirmed = Format(trackedSourceContext.Network, confirmed),
				Unconfirmed = Format(trackedSourceContext.Network, unconfirmed),
				Available = Format(trackedSourceContext.Network, available),
				Total = Format(trackedSourceContext.Network, total),
				Immature = Format(trackedSourceContext.Network, immature)
			};
			balance.Total = balance.Confirmed.Add(balance.Unconfirmed);
			return Json(balance, trackedSourceContext.Network.JsonSerializerSettings);
		}		
		
		[HttpPost("associate")]
		[PostgresImplementationActionConstraint(true)]
		public async Task<IActionResult> AssociateScripts( TrackedSourceContext trackedSourceContext, [FromBody] JArray rawRequest)
		{
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			var jsonSerializer = JsonSerializer.Create(trackedSourceContext.Network.JsonSerializerSettings);
			var requests = rawRequest.ToObject<AssociateScriptRequest[]>(jsonSerializer);
			await repo.AssociateScriptsToWalletExplicitly(trackedSourceContext.TrackedSource, requests);
			return Ok();
		}
		
		[HttpPost("import-utxos")]
		[TrackedSourceContext.TrackedSourceContextRequirement(true)]
		public async Task<IActionResult> ImportUTXOs( TrackedSourceContext trackedSourceContext, [FromBody] JObject rawRequest)
		{
			
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			var jsonSerializer = JsonSerializer.Create(trackedSourceContext.Network.JsonSerializerSettings);
			var request = rawRequest.ToObject<ImportUTXORequest>(jsonSerializer);

			if (request.Utxos?.Any() is not true)
				return Ok();
			
			var rpc = trackedSourceContext.RpcClient;

			var clientBatch = rpc.PrepareBatch();
			var coinToTxOut = new Dictionary<OutPoint, Task<GetTxOutResponse>>();
			var txsToBlockHash = new ConcurrentDictionary<uint256, uint256>();
			var blockHeaders = new ConcurrentDictionary<uint256, Task<(uint256 hash, DateTimeOffset time)>>();
			var allUtxoTransactionHashes  = request.Utxos.Select(u => u.Hash).Distinct().ToArray();
			foreach (var importUtxoRequest in request.Utxos)
			{
				coinToTxOut.TryAdd(importUtxoRequest, clientBatch.GetTxOutAsync(importUtxoRequest.Hash, (int)importUtxoRequest.N));
			}
			request.Proofs ??= Array.Empty<MerkleBlock>();
			var verifyTasks = request.Proofs
				.Where(p => p is not null && p.PartialMerkleTree.Hashes.Any(uint256 => allUtxoTransactionHashes.Contains(uint256)))
				.Select(async proof =>
				{
					var txoutproofResult = await clientBatch.SendCommandAsync("verifytxoutproof",
						Encoders.Hex.EncodeData(proof.ToBytes()));
					if (txoutproofResult.Error is not null && txoutproofResult.Result is JArray prooftxs)
					{
						foreach (var txProof in prooftxs)
						{
							var txId = uint256.Parse(txProof.Value<string>());
							blockHeaders.TryAdd(proof.Header.GetHash(), Task.FromResult((proof.Header.GetHash(), proof.Header.BlockTime)));
							txsToBlockHash.TryAdd(txId, proof.Header.GetHash());
						}
					}
				});

			await clientBatch.SendBatchAsync();
			await Task.WhenAll(verifyTasks.Concat(coinToTxOut.Values));
			
			
			coinToTxOut =  coinToTxOut.Where(c => c.Value.Result is not null).ToDictionary(pair => pair.Key, pair => pair.Value);

			await using var conn = await repo.ConnectionFactory.CreateConnection();
		
			 var blockTasks = new ConcurrentDictionary<uint256, Task<int>>();
			 var blocksToRequest = new HashSet<uint256>();
			foreach (var cTxOut in coinToTxOut)
			{
				var result = await cTxOut.Value;
				if (result.Confirmations == 1)
				{
					txsToBlockHash.TryAdd(cTxOut.Key.Hash, result.BestBlock);
					continue;
				}
				blocksToRequest.Add(result.BestBlock);
			}

			var res = await conn.QueryAsync(
				$"SELECT blk_id, height FROM blks WHERE code=@code AND blk_id IN (SELECT unnest(@blkIds)) ",
				new
				{
					code = trackedSourceContext.Network.CryptoCode,
					blkIds = blocksToRequest.Select(uint256 => uint256.ToString()).ToArray()
				});

			foreach (var r in res)
			{
				var blockHash = uint256.Parse((string)r.blk_id);
				var height = (int)r.height;
				blockTasks.TryAdd(blockHash, Task.FromResult(height));
				blocksToRequest.Remove(blockHash);
			}

			clientBatch = rpc.PrepareBatch();
			foreach (var bh in blocksToRequest)
			{
				blockTasks.TryAdd(bh,  clientBatch.GetBlockHeaderAsyncEx(bh).ContinueWith(task => task.Result.Height));
			}
			await clientBatch.SendBatchAsync();
			await Task.WhenAll(blockTasks.Values);
			var heightToBlockHash = new ConcurrentDictionary<int, Task<(uint256 hash, DateTimeOffset time)>>();
			var heightsToFetch = new HashSet<int>();
			foreach (var cTxOut in coinToTxOut)	
			{
				var result = await cTxOut.Value;
				
				if (result.Confirmations <= 1)
					continue;

				blockTasks.TryGetValue(result.BestBlock, out var blockTask);
				var b = await blockTask;

				var heightToFetch = b - result.Confirmations - 1;
				heightsToFetch.Add(heightToFetch);
			}
			
			res = await conn.QueryAsync(
				$"SELECT blk_id, height, indexed_at FROM blks WHERE code=@code AND height IN (SELECT unnest(@heights)) ",
				new
				{
					code = trackedSourceContext.Network.CryptoCode,
					heights = heightsToFetch.ToArray()
				});

			foreach (var r in res)
			{
				var blockHash = uint256.Parse((string)r.blk_id);
				var height = (int)r.height;
				var blockTime = (DateTimeOffset)r.indexed_at;
				blockTasks.TryAdd(blockHash, Task.FromResult(height));
				heightToBlockHash.TryAdd(height, Task.FromResult((blockHash, blockTime)));
				heightsToFetch.Remove((int)r.height);
			}

			foreach (var heightToFetch in heightsToFetch)
			{
				heightToBlockHash.TryAdd(heightToFetch, clientBatch.GetBlockHeaderAsync(heightToFetch).ContinueWith(task => (task.Result.GetHash(), task.Result.BlockTime)));
			}
			
			clientBatch = rpc.PrepareBatch();
			
			await clientBatch.SendBatchAsync();
			foreach (var htbh in heightToBlockHash.Values)
			{
				var result = await htbh;
				blockHeaders.TryAdd(result.hash, Task.FromResult(result));

				foreach (var cto in coinToTxOut)
				{
					var result2 = await cto.Value;
					if (result2.Confirmations <= 1)
						continue;
					
					txsToBlockHash.TryAdd(cto.Key.Hash, result.hash);
				}
			}

			var now = DateTimeOffset.UtcNow;

			var scripts = coinToTxOut
				.Select(pair => (
					pair.Value.Result.TxOut.ScriptPubKey.GetDestinationAddress(repo.Network.NBitcoinNetwork), pair))
				.Where(pair => pair.Item1 is not null).Select(tuple => new AssociateScriptRequest()
				{
					Destination = tuple.Item1,
					Used = tuple.pair.Value is not null,
					Metadata = null
				}).ToArray();
			
			await repo.AssociateScriptsToWalletExplicitly(trackedSourceContext.TrackedSource,scripts);


			var trackedTransactions = coinToTxOut.Select(async pair =>
			{
				var txOutResult = await pair.Value;
				txsToBlockHash.TryGetValue(pair.Key.Hash, out var blockHash);
				(uint256 hash, DateTimeOffset time)? blockHeader = null;
				if (blockHash is not null && blockHeaders.TryGetValue(blockHash, out var blockHeaderT))
				{
					blockHeader = await blockHeaderT;
				};
					
				var coin = new Coin(pair.Key, txOutResult.TxOut);
				
				var ttx = repo.CreateTrackedTransaction(trackedSourceContext.TrackedSource,
					new TrackedTransactionKey(pair.Key.Hash, blockHash, true){},
					new[] {coin}, null);
				ttx.Inserted = now;
				ttx.Immature =txOutResult.IsCoinBase && txOutResult.Confirmations <= 100;
				ttx.FirstSeen = blockHeader?.time?? NBitcoin.Utils.UnixTimeToDateTime(0);;
				return ttx;
			});
			
			await repo.SaveMatches(await Task.WhenAll(trackedTransactions));
			
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
		public async Task<IActionResult> GetUTXOs( TrackedSourceContext trackedSourceContext)
		{
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			await using var conn = await ConnectionFactory.CreateConnection();
			var height = await conn.ExecuteScalarAsync<long>("SELECT height FROM get_tip(@code)", new { code = trackedSourceContext.Network.CryptoCode });
			// On elements, we can't get blinded address from the scriptPubKey, so we need to fetch it rather than compute it
			string addrColumns = "NULL as address";
			var derivationScheme = (trackedSourceContext.TrackedSource as DerivationSchemeTrackedSource)
				?.DerivationStrategy;
			if (trackedSourceContext.Network.IsElement && derivationScheme?.Unblinded() is true)
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
				$"FROM wallets_utxos wu{descriptorJoin} WHERE code=@code AND wallet_id=@walletId AND immature IS FALSE", new { code =trackedSourceContext.Network.CryptoCode, walletId = repo.GetWalletKey(trackedSourceContext.TrackedSource).wid }));
			UTXOChanges changes = new UTXOChanges()
			{
				CurrentHeight = (int)height,
				TrackedSource = trackedSourceContext.TrackedSource,
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
				u.Address = utxo.address is null ? u.ScriptPubKey.GetDestinationAddress(trackedSourceContext.Network.NBitcoinNetwork) : BitcoinAddress.Create(utxo.address, trackedSourceContext.Network.NBitcoinNetwork);
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
			return Json(changes, trackedSourceContext.Network.JsonSerializerSettings);
		}
		
		
		[HttpGet("children")]
		public async Task<IActionResult> GetWalletChildren( TrackedSourceContext trackedSourceContext)
		{
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			await using var conn = await ConnectionFactory.CreateConnection();
			var children = await conn.QueryAsync($"SELECT w.wallet_id, w.metadata FROM wallets_wallets ww JOIN wallets w ON ww.wallet_id = w.wallet_id WHERE ww.parent_id=@walletId", new {  walletId = repo.GetWalletKey(trackedSourceContext.TrackedSource).wid });
			
			return Json(children.Select(c =>  repo.GetTrackedSource(new PostgresRepository.WalletKey(c.wallet_id, c.metadata)) ).ToArray(), trackedSourceContext.Network.JsonSerializerSettings);
		}
		[HttpGet("parents")]
		public async Task<IActionResult> GetWalletParents( TrackedSourceContext trackedSourceContext)
		{
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			await using var conn = await ConnectionFactory.CreateConnection();
			var children = await conn.QueryAsync($"SELECT w.wallet_id, w.metadata FROM wallets_wallets ww JOIN wallets w ON ww.parent_id = w.wallet_id WHERE ww.wallet_id=@walletId", new {  walletId = repo.GetWalletKey(trackedSourceContext.TrackedSource).wid });
			
			return Json(children.Select(c =>  repo.GetTrackedSource(new PostgresRepository.WalletKey(c.wallet_id, c.metadata)) ).ToArray(), trackedSourceContext.Network.JsonSerializerSettings);
		}
		[HttpPost("children")]
		public async Task<IActionResult> AddWalletChild( TrackedSourceContext trackedSourceContext, [FromBody] JObject request)
		{
			var trackedSource = trackedSourceContext.Network.ParseJObject<TrackedSourceRequest>(request).TrackedSource;
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			await repo.EnsureWalletCreated(trackedSource, trackedSourceContext.TrackedSource);
			return Ok();
		}
		[HttpPost("parents")]
		public async Task<IActionResult> AddWalletParent( TrackedSourceContext trackedSourceContext, [FromBody] JObject request)
		{
			var trackedSource = trackedSourceContext.Network.ParseJObject<TrackedSourceRequest>(request).TrackedSource;
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			await repo.EnsureWalletCreated(trackedSourceContext.TrackedSource, trackedSource);
			return Ok();
		}
		[HttpDelete("children")]
		public async Task<IActionResult> RemoveWalletChild( TrackedSourceContext trackedSourceContext, [FromBody] JObject request)
		{
			var repo = (PostgresRepository)trackedSourceContext.Repository;

			var trackedSource = repo.GetWalletKey(trackedSourceContext.Network
				.ParseJObject<TrackedSourceRequest>(request).TrackedSource);
			var conn = await ConnectionFactory.CreateConnection();
			await conn.ExecuteAsync($"DELETE FROM wallets_wallets WHERE wallet_id=@walletId AND parent_id=@parentId", new { walletId = trackedSource.wid, parentId = repo.GetWalletKey(trackedSourceContext.TrackedSource).wid });
			return Ok();
		}
		[HttpDelete("parents")]
		public async Task<IActionResult> RemoveWalletParent( TrackedSourceContext trackedSourceContext, [FromBody] JObject request)
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
