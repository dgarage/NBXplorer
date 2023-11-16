using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
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
		public async Task<IActionResult> AssociateScripts( TrackedSourceContext trackedSourceContext,
			[FromBody] Dictionary<string, bool> scripts)
		{
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			await repo.AssociateScriptsToWalletExplicitly(trackedSourceContext.TrackedSource,
				scripts.ToDictionary(pair => (IDestination) BitcoinAddress.Create(pair.Key, trackedSourceContext.Network.NBitcoinNetwork),
					pair => pair.Value));
			return Ok();
		}
		
		[HttpPost("import-utxos")]
		[TrackedSourceContext.TrackedSourceContextRequirement(true)]
		public async Task<IActionResult> ImportUTXOs( TrackedSourceContext trackedSourceContext, [FromBody] JArray rawRequest)
		{
			var jsonSerializer = JsonSerializer.Create(trackedSourceContext.Network.JsonSerializerSettings);
			var coins = rawRequest.ToObject<ImportUTXORequest[]>(jsonSerializer)?.Where(c => c.Coin != null).ToArray();
			if (coins?.Any() is not true)
				throw new ArgumentNullException(nameof(coins));
			
			var repo = (PostgresRepository)trackedSourceContext.Repository;
			var rpc = trackedSourceContext.RpcClient;

			var clientBatch = rpc.PrepareBatch();
			var coinToTxOut = new ConcurrentDictionary<Coin, GetTxOutResponse>();
			var coinToBlock = new ConcurrentDictionary<Coin, BlockHeader>();
			await Task.WhenAll(coins.SelectMany(o =>
			{
				return new[]
				{
					Task.Run(async () =>
					{
						var txOutResponse =
							await clientBatch.GetTxOutAsync(o.Coin.Outpoint.Hash, (int) o.Coin.Outpoint.N);
						if (txOutResponse is not null)
							coinToTxOut.TryAdd(o.Coin, txOutResponse);
					}),
					Task.Run(async () =>
					{
						if (o.Proof is not null && o.Proof.PartialMerkleTree.Hashes.Contains(o.Coin.Outpoint.Hash))
						{
							// var merkleBLockProofBytes = Encoders.Hex.DecodeData(o.TxOutProof);
							// var mb = new MerkleBlock();
							// mb.FromBytes(merkleBLockProofBytes);
							// mb.ReadWrite(merkleBLockProofBytes, network.NBitcoinNetwork);

							var txoutproofResult =
								await clientBatch.SendCommandAsync("verifytxoutproof", o.Proof);

							var txHash = o.Coin.Outpoint.Hash.ToString();
							if (txoutproofResult.Error is not null && txoutproofResult.Result is JArray prooftxs &&
							    prooftxs.Any(token =>
								    token.Value<string>()
									    ?.Equals(txHash, StringComparison.InvariantCultureIgnoreCase) is true))
							{
								coinToBlock.TryAdd(o.Coin, o.Proof.Header);
							}
						}
					})
				};
			}).Concat(new[] {clientBatch.SendBatchAsync()}).ToArray());

			DateTimeOffset now = DateTimeOffset.UtcNow;
			await repo.SaveMatches(coinToTxOut.Select(pair =>
			{
				coinToBlock.TryGetValue(pair.Key, out var blockHeader);
				var ttx = repo.CreateTrackedTransaction(trackedSourceContext.TrackedSource,
					new TrackedTransactionKey(pair.Key.Outpoint.Hash, blockHeader?.GetHash(), true){},
					new[] {pair.Key}, null);
				ttx.Inserted = now;
				ttx.FirstSeen = blockHeader?.BlockTime?? NBitcoin.Utils.UnixTimeToDateTime(0);;
				return ttx;
			}).ToArray());
			
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
	}
}
