using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System.Threading.Tasks;
using System;
using System.Linq;
using Dapper;
using NBXplorer.Backend;
using Newtonsoft.Json.Linq;

namespace NBXplorer.Controllers
{
	[Route($"v1/{CommonRoutes.DerivationEndpoint}")]
	[Route($"v1/{CommonRoutes.AddressEndpoint}")]
	[Route($"v1/{CommonRoutes.BaseCryptoEndpoint}/{CommonRoutes.GroupEndpoint}")]
	[Authorize]
	public class CommonRoutesController : Controller
	{
		public DbConnectionFactory ConnectionFactory { get; }
		public CommonRoutesController(DbConnectionFactory connectionFactory)
		{
			ConnectionFactory = connectionFactory;
		}
		[HttpGet("balance")]
		public async Task<IActionResult> GetBalance(TrackedSourceContext trackedSourceContext)
		{
			var trackedSource = trackedSourceContext.TrackedSource;
			var network = trackedSourceContext.Network;
			var repo = trackedSourceContext.Repository;
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
			var repo = trackedSourceContext.Repository;
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


		[HttpPost("metadata/{key}")]
		[HttpPost($"~/v1/{CommonRoutes.GroupEndpoint}/metadata/{{key}}")]
		public async Task<IActionResult> SetMetadata(TrackedSourceContext trackedSourceContext, string key, [FromBody] JToken value = null)
		{
			await trackedSourceContext.Repository.SaveMetadata(trackedSourceContext.TrackedSource, key, value);
			return Ok();
		}

		[HttpGet("metadata/{key}")]
		[HttpGet($"~/v1/{CommonRoutes.GroupEndpoint}/metadata/{{key}}")]
		public async Task<IActionResult> GetMetadata(TrackedSourceContext trackedSourceContext, string key)
		{
			var result = await trackedSourceContext.Repository.GetMetadata<JToken>(trackedSourceContext.TrackedSource, key);
			return result == null ? NotFound() : Json(result, trackedSourceContext.Repository.Serializer.Settings);
		}
	}
}
