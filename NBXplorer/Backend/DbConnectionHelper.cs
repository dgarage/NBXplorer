#nullable enable
using Dapper;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NBXplorer.Backend
{
	public class DbConnectionHelper : IDisposable, IAsyncDisposable
	{
		public DbConnectionHelper(NBXplorerNetwork network,
									DbConnection connection)
		{
			derivationStrategyFactory = new DerivationStrategyFactory(network.NBitcoinNetwork);
			Network = network;
			Connection = connection;
		}
		DerivationStrategyFactory derivationStrategyFactory;

		public NBXplorerNetwork Network { get; }
		public DbConnection Connection { get; }
		public int MinPoolSize { get; set; }
		public int MaxPoolSize { get; set; }

		public void Dispose()
		{
			Connection.Dispose();
		}

		public ValueTask DisposeAsync()
		{
			return Connection.DisposeAsync();
		}

		public record NewOut(uint256 txId, int idx, Script script, IMoney value)
		{
			public static NewOut FromCoin(ICoin c)
				=> new(c.Outpoint.Hash, (int)c.Outpoint.N, c.TxOut.ScriptPubKey, c.Amount);
		}
		public record NewIn(uint256 txId, int idx, uint256 spentTxId, int spentIdx);
		public record NewOutRaw(string tx_id, long idx, string script, long value, string asset_id);
		public record NewInRaw(string tx_id, long idx, string spent_tx_id, long spent_idx);
		internal record OutpointRaw(string tx_id, long idx);

		public static void Register(NpgsqlDataSourceBuilder dsBuilder)
		{
			dsBuilder.MapComposite<NewOutRaw>("new_out");
			dsBuilder.MapComposite<NewInRaw>("new_in");
			dsBuilder.MapComposite<OutpointRaw>("outpoint");
			dsBuilder.MapComposite<Repository.DescriptorScriptInsert>("nbxv1_ds");
		}
		public async Task<bool> FetchMatches(MatchQuery matchQuery, CancellationToken cancellationToken)
		{
			var outs = new List<NewOutRaw>(matchQuery.Outs.Count);
			var ins = new List<NewInRaw>(matchQuery.Ins.Count);
			foreach (var o in matchQuery.Outs)
			{
				long value;
				string assetId;
				if (o.value is Money m)
				{
					value = m.Satoshi;
					assetId = string.Empty;
				}
				else if (o.value is AssetMoney am)
				{
					value = am.Quantity;
					assetId = am.AssetId.ToString();
				}
				else
				{
					value = NBXplorerNetwork.UnknownAssetMoney.Quantity;
					assetId = NBXplorerNetwork.UnknownAssetId;
				}
				outs.Add(new NewOutRaw(o.txId.ToString(), o.idx, o.script.ToHex(), value, assetId));
			}
			foreach (var ni in matchQuery.Ins)
			{
				ins.Add(new NewInRaw(ni.txId.ToString(), ni.idx, ni.spentTxId.ToString(), ni.spentIdx));
			}

			DynamicParameters parameters = new DynamicParameters();
			parameters.Add("in_code", Network.CryptoCode);
			parameters.Add("in_outs", outs);
			parameters.Add("in_ins", ins);
			parameters.Add("has_match", dbType: System.Data.DbType.Boolean, direction: ParameterDirection.InputOutput);
			var command = new CommandDefinition(
				commandText: "fetch_matches",
				parameters: parameters,
				commandType: CommandType.StoredProcedure,
				commandTimeout: ((NpgsqlConnection)Connection).CommandTimeout * 3,
				cancellationToken: cancellationToken
				);
			await Connection.QueryAsync<int>(command);
			return parameters.Get<bool>("has_match");
		}

		public record SaveTransactionRecord(Transaction? Transaction, uint256 Id, uint256? BlockId, int? BlockIndex, long? BlockHeight, bool Immature, DateTimeOffset SeenAt)
		{
			public static SaveTransactionRecord Create(Transaction? tx = null, uint256? txHash = null, SlimChainedBlock? slimBlock = null, int? blockIndex = null, DateTimeOffset? seenAt = null) => new SaveTransactionRecord(
						tx,
						txHash ?? tx?.GetHash() ?? throw new ArgumentException("tx or txHash is expected"),
						slimBlock?.Hash,
						blockIndex,
						slimBlock?.Height,
						tx?.IsCoinBase is true,
						seenAt ?? DateTimeOffset.UtcNow
					);
		}

		public async Task SaveTransactions(IEnumerable<SaveTransactionRecord> transactions)
		{
			var parameters = transactions
				.DistinctBy(o => o.Id)
				.Select(tx =>
			new
			{
				code = Network.CryptoCode,
				blk_id = tx.BlockId?.ToString(),
				id = tx.Id?.ToString() ?? tx.Transaction?.GetHash()?.ToString(),
				raw = tx.Transaction?.ToBytes(),
				mempool = tx.BlockId is null,
				seen_at = tx.SeenAt,
				blk_idx = tx.BlockIndex is int i ? i : 0,
				blk_height = tx.BlockHeight,
				immature = tx.Immature
			})
			.Where(o => o.id is not null)
			.ToArray();
			await Connection.ExecuteAsync("INSERT INTO txs(code, tx_id, raw, immature, seen_at) VALUES (@code, @id, @raw, @immature, COALESCE(@seen_at, CURRENT_TIMESTAMP)) " +
										  " ON CONFLICT (code, tx_id) " +
										  " DO UPDATE SET seen_at=LEAST(COALESCE(@seen_at, CURRENT_TIMESTAMP), txs.seen_at), raw = COALESCE(@raw, txs.raw), immature=EXCLUDED.immature", parameters);
			await Connection.ExecuteAsync("INSERT INTO blks_txs VALUES (@code, @blk_id, @id, @blk_idx) ON CONFLICT DO NOTHING", parameters.Where(p => p.blk_id is not null).AsList());
		}

		public async Task MakeOrphanFrom(int height)
		{
			await Connection.ExecuteAsync("UPDATE blks SET confirmed='f' WHERE code=@code AND height >= @height;", new { code = Network.CryptoCode, height = height });
		}

		public async Task<Dictionary<OutPoint, TxOut>> GetOutputs(IEnumerable<OutPoint> outPoints)
		{
			outPoints.TryGetNonEnumeratedCount(out var outpointCount);
			List<OutpointRaw> rawOutpoints = new List<OutpointRaw>(outpointCount);
			foreach (var o in outPoints)
				rawOutpoints.Add(new OutpointRaw(o.Hash.ToString(), o.N));
			Dictionary <OutPoint, TxOut> result = new Dictionary<OutPoint, TxOut>();
			foreach (var r in await Connection.QueryAsync<(string tx_id, long idx, string script, long value, string asset_id)>(
				"SELECT o.tx_id, o.idx, o.script, o.value, o.asset_id FROM unnest(@outpoints) outpoints " +
				"JOIN outs o ON code=@code AND o.tx_id=outpoints.tx_id AND o.idx=outpoints.idx",
				new
				{
					code = Network.CryptoCode,
					outpoints = rawOutpoints
				}))
			{
				var txout = this.Network.NBitcoinNetwork.Consensus.ConsensusFactory.CreateTxOut();
				txout.Value = Money.Satoshis(r.value);
				txout.ScriptPubKey = Script.FromHex(r.script);
				result.TryAdd(new OutPoint(uint256.Parse(r.tx_id), (uint)r.idx), txout);
			}
			return result;
		}

		public async Task<SlimChainedBlock> GetTip()
		{
			var row = await Connection.QueryFirstOrDefaultAsync("SELECT * FROM get_tip(@code);", new { code = Network.CryptoCode });
			if (row is null)
				return CreateGenesis();
			return new SlimChainedBlock(uint256.Parse(row.blk_id), row.height == 0
				? null
				: uint256.Parse(row.prev_id), (int)row.height);
		}

		private SlimChainedBlock CreateGenesis()
		{
			return new SlimChainedBlock(Network.NBitcoinNetwork.Consensus.HashGenesisBlock, null, 0);
		}

		public async Task<bool> SetMetadata<TMetadata>(string walletId, string key, TMetadata value) where TMetadata : class
		{
			if (value is null)
				return await Connection.ExecuteAsync("DELETE FROM nbxv1_metadata WHERE wallet_id=@walletId AND key=@key", new { walletId, key }) == 1;
			else
				return await Connection.ExecuteAsync("INSERT INTO nbxv1_metadata VALUES (@walletId, @key, @data::JSONB) ON CONFLICT (wallet_id, key) DO UPDATE SET data=EXCLUDED.data;", new { walletId, key, data = Network.Serializer.ToString(value) }) == 1;
		}
		public async Task<TMetadata?> GetMetadata<TMetadata>(string walletId, string key) where TMetadata : class
		{
			var result = await Connection.ExecuteScalarAsync<string?>("SELECT data FROM nbxv1_metadata WHERE wallet_id=@walletId AND key=@key", new { walletId, key });
			if (result is null)
				return null;
			return Network.Serializer.ToObject<TMetadata>(result);
		}
		public async Task<HashSet<uint256>> GetUnconfirmedTxs()
		{
			var txs = await Connection.QueryAsync<string>("SELECT tx_id FROM txs WHERE code=@code AND mempool IS TRUE;", new { code = Network.CryptoCode });
			return new HashSet<uint256>(txs.Select(t => uint256.Parse(t)));
		}

		public async Task NewBlock(SlimChainedBlock newTip)
		{
			var parameters = new
			{
				code = Network.CryptoCode,
				id = newTip.Hash.ToString(),
				prev = newTip.Previous.ToString(),
				height = newTip.Height
			};
			await Connection.ExecuteAsync("INSERT INTO blks VALUES (@code, @id, @height, @prev) ON CONFLICT DO NOTHING;", parameters);
		}

		public async Task NewBlockCommit(uint256 blockHash)
		{
			await Connection.ExecuteAsync("UPDATE blks SET confirmed='t' WHERE code=@code AND blk_id=@blk_id AND confirmed IS FALSE;",
				new
				{
					code = Network.CryptoCode,
					blk_id = blockHash.ToString()
				});
		}
	}
}
