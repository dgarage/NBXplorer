using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBXplorer.Backend;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using static NBXplorer.Backend.Repository;

namespace NBXplorer.Controllers
{
	[Route($"v1")]
	[PostgresImplementationActionConstraint(true)]
	[Authorize]
	public class GroupsController : Controller
	{
		public GroupsController(
		DbConnectionFactory connectionFactory,
		NBXplorerNetworkProvider networkProvider)
		{
			ConnectionFactory = connectionFactory;
			NetworkProvider = networkProvider;
		}
		public DbConnectionFactory ConnectionFactory { get; }
		public NBXplorerNetworkProvider NetworkProvider { get; }

		[HttpPost(CommonRoutes.BaseGroupEndpoint)]
		public async Task<IActionResult> CreateGroup()
		{
			var group = GroupTrackedSource.Generate();
			await using var conn = await ConnectionFactory.CreateConnection();
			await conn.ExecuteAsync(Repository.WalletInsertQuery, Repository.GetWalletKey(group));
			return base.Ok(ToGroupInfo(group));
		}

		[HttpGet(CommonRoutes.GroupEndpoint)]
		public async Task<IActionResult> GetGroup(string groupId)
		{
			var group = new GroupTrackedSource(groupId);
			var w = Repository.GetWalletKey(new GroupTrackedSource(groupId));
			await using var conn = await ConnectionFactory.CreateConnection();
			var children = (await conn.QueryAsync<WalletKey>(
				"SELECT wc.wallet_id wid, wc.metadata FROM wallets w " +
				"LEFT JOIN wallets_wallets ww ON ww.parent_id=w.wallet_id " +
				"LEFT JOIN wallets wc ON wc.wallet_id=ww.wallet_id " +
				"WHERE w.wallet_id=@wid", new { w.wid })).ToArray();
			if (children.Length == 0)
				throw GroupNotFound();
			var groupInfo = ToGroupInfo(group);
			if (!(children.Length is 1 && children[0].wid is null))
				groupInfo.Children = ToGroupChildren(children);
			return Ok(groupInfo);
		}

		private static NBXplorerException GroupNotFound()
		{
			return new NBXplorerException(new NBXplorerError(404, "group-not-found", "The group doesn't exist"));
		}

		private GroupChild[] ToGroupChildren(WalletKey[] children) => children.Select(x => ToGroupChild(x)).Where(x => x is not null).ToArray();

		private GroupChild ToGroupChild(WalletKey walletKey)
		{
			if (walletKey is null)
				return null;
			var cryptoCode = JObject.Parse(walletKey.metadata)["code"]?.Value<string>();
			NBXplorerNetwork net = null;
			if (cryptoCode != null)
			{
				net = NetworkProvider.GetFromCryptoCode(cryptoCode);
				if (net is null)
					return null;
			}
			var trackedSource = Repository.TryGetTrackedSource(walletKey, net);
			return new GroupChild() { CryptoCode = net?.CryptoCode, TrackedSource = trackedSource.ToString() };
		}

		[HttpPost($"{CommonRoutes.GroupEndpoint}/children")]
		[HttpDelete($"{CommonRoutes.GroupEndpoint}/children")]
		public async Task<IActionResult> AddDeleteGroupChildren(string groupId, [FromBody] GroupChild[] children)
		{
			var w = Repository.GetWalletKey(new GroupTrackedSource(groupId));
			await using (var conn = await ConnectionFactory.CreateConnection())
			{
				var rows = children
						.Select(c => GetWid(c))
						.Where(c => c is not null)
						.Select(c => new { child = c, w.wid }).ToArray();
				if (HttpContext.Request.Method == "POST")
					try
					{
						await conn.ExecuteAsync("INSERT INTO wallets_wallets VALUES (@child, @wid) ON CONFLICT (wallet_id, parent_id) DO NOTHING", rows);
					}
					catch (NpgsqlException ex) when (ex.SqlState == PostgresErrorCodes.RaiseException)
					{
						throw new NBXplorerException(new NBXplorerError(409, "cycle-detected", "A cycle has been detected"));
					}
					catch (NpgsqlException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
					{
						throw GroupNotFound();
					}
				if (HttpContext.Request.Method == "DELETE")
					await conn.ExecuteAsync("DELETE FROM wallets_wallets WHERE wallet_id=@child AND parent_id=@wid;", rows);
			}
			return await GetGroup(groupId);
		}

		[HttpPost($"{CommonRoutes.BaseCryptoEndpoint}/{CommonRoutes.GroupEndpoint}/addresses")]
		public async Task<IActionResult> AddGroupAddress(TrackedSourceContext trackedSourceContext, [FromBody] string[] addresses)
		{
			var group = (GroupTrackedSource)trackedSourceContext.TrackedSource;
			IList<DescriptorScriptInsert> rows;
			try
			{
				rows = addresses
					.Where(a => a is not null)
					.Select(a => BitcoinAddress.Create(a, trackedSourceContext.Network.NBitcoinNetwork))
					.Select(a => new DescriptorScriptInsert("", 0, a.ScriptPubKey.ToHex(), "{}", a.ToString(), false))
					.ToList();
			}
			catch (FormatException)
			{
				throw new NBXplorerException(new NBXplorerError(400, "invalid-address",
					$"An address cannot be parsed"));
			}
			await using (var conn = await ConnectionFactory.CreateConnection())
			{
				await conn.ExecuteAsync(Repository.InsertScriptsScript +
					"INSERT INTO wallets_scripts (code, script, wallet_id) SELECT @code code, script, @wid FROM unnest(@records) ON CONFLICT DO NOTHING;",
					new
					{
						code = trackedSourceContext.Network.CryptoCode,
						wid = Repository.GetWalletKey(group).wid,
						records = rows
					});
			}
			return Ok();
		}

		private string GetWid(GroupChild c)
		{
			if (c?.TrackedSource is null)
				return null;
			var net = c.CryptoCode is null ? null : NetworkProvider.GetFromCryptoCode(c.CryptoCode);
			if (c.TrackedSource.StartsWith("ADDRESS:") || c.TrackedSource.StartsWith("DERIVATIONSCHEME:") && c.CryptoCode is null)
				throw new NBXplorerException(new NBXplorerError(400, "invalid-group-child", "ADDRESS: and DERIVATIONSCHEME: tracked sources must also include a cryptoCode parameter"));
			if (!TrackedSource.TryParse(c.TrackedSource, out var ts, net))
				throw new NBXplorerException(new NBXplorerError(400, "invalid-group-child", "Invalid tracked source format"));
			return Repository.GetWalletKey(ts, net)?.wid;
		}

		private static GroupInformation ToGroupInfo(GroupTrackedSource group)
		{
			return new GroupInformation()
			{
				TrackedSource = group.ToString(),
				GroupId = group.GroupId,
				Children = Array.Empty<GroupChild>()
			};
		}
	}
}
