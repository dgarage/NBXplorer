#nullable enable
using Dapper;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer.Configuration;
using NBXplorer.DerivationStrategy;
using NBXplorer.Logging;
using NBXplorer.Models;
using Npgsql;
using Npgsql.TypeMapping;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NBXplorer.Backends.Postgres
{
	public class DbConnectionHelper : IDisposable, IAsyncDisposable
	{
		public DbConnectionHelper(NBXplorerNetwork network,
									DbConnection connection,
									KeyPathTemplates keyPathTemplates)
		{
			derivationStrategyFactory = new DerivationStrategyFactory(network.NBitcoinNetwork);
			Network = network;
			Connection = connection;
			KeyPathTemplates = keyPathTemplates;
		}
		DerivationStrategyFactory derivationStrategyFactory;

		public NBXplorerNetwork Network { get; }
		public DbConnection Connection { get; }
		public KeyPathTemplates KeyPathTemplates { get; }
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

		public record NewOut(uint256 txId, int idx, Script script, IMoney value);
		public record NewIn(uint256 txId, int idx, uint256 spentTxId, int spentIdx);
		public record NewOutRaw(string tx_id, long idx, string script, long value, string asset_id);
		public record NewInRaw(string tx_id, long idx, string spent_tx_id, long spent_idx);
		internal record OutpointRaw(string tx_id, long idx);

		public static void Register(INpgsqlTypeMapper typeMapper)
		{
			typeMapper.MapComposite<NewOutRaw>("new_out");
			typeMapper.MapComposite<NewInRaw>("new_in");
			typeMapper.MapComposite<OutpointRaw>("outpoint");
			typeMapper.MapComposite<PostgresRepository.DescriptorScriptInsert>("nbxv1_ds");
		}

		public class FetchOptions
		{
			public Money? MinUtxoValue { get; set; }
		}
		public Task<bool> FetchMatches(IEnumerable<Transaction> txs, SlimChainedBlock slimBlock, Money? minUtxoValue)
		{
			var outCount = txs.Select(t => t.Outputs.Count).Sum();
			List<DbConnectionHelper.NewOut> outs = new List<DbConnectionHelper.NewOut>(outCount);
			var inCount = txs.Select(t => t.Inputs.Count).Sum();
			List<DbConnectionHelper.NewIn> ins = new List<DbConnectionHelper.NewIn>(inCount);
			foreach (var tx in txs)
			{
				if (!tx.IsCoinBase)
				{
					int i = 0;
					foreach (var input in tx.Inputs)
					{
						ins.Add(new DbConnectionHelper.NewIn(tx.GetHash(), i, input.PrevOut.Hash, (int)input.PrevOut.N));
						i++;
					}
				}
				int io = -1;
				foreach (var output in tx.Outputs)
				{
					io++;
					if (minUtxoValue != null && output.Value < minUtxoValue)
						continue;
					outs.Add(new DbConnectionHelper.NewOut(tx.GetHash(), io, output.ScriptPubKey, output.Value));
				}
			}

			return FetchMatches(outs, ins);
		}
		public async Task<bool> FetchMatches(IEnumerable<NewOut>? newOuts, IEnumerable<NewIn>? newIns)
		{
			newOuts ??= Array.Empty<NewOut>();
			newIns ??= Array.Empty<NewIn>();
			newOuts.TryGetNonEnumeratedCount(out int outCount);
			newIns.TryGetNonEnumeratedCount(out int inCount);

			var outs = new List<NewOutRaw>(outCount);
			var ins = new List<NewInRaw>(inCount);
			foreach (var o in newOuts)
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
			foreach (var ni in newIns)
			{
				ins.Add(new NewInRaw(ni.txId.ToString(), ni.idx, ni.spentTxId.ToString(), ni.spentIdx));
			}
			return await Connection.ExecuteScalarAsync<bool>("CALL fetch_matches(@code, @outs, @ins, 'f');", new { code = Network.CryptoCode, outs = outs, ins = ins });
		}
		public async Task SaveTransactions(IEnumerable<(Transaction? Transaction, uint256? Id, uint256? BlockId, int? BlockIndex, long? BlockHeight, bool immature)> transactions, DateTimeOffset? now)
		{
			var parameters = transactions.Select(tx =>
			new
			{
				code = Network.CryptoCode,
				blk_id = tx.BlockId?.ToString(),
				id = tx.Id?.ToString() ?? tx.Transaction?.GetHash()?.ToString(),
				raw = tx.Transaction?.ToBytes(),
				mempool = tx.BlockId is null,
				seen_at = now is null ? default : now.Value,
				blk_idx = tx.BlockIndex is int i ? i : 0,
				blk_height = tx.BlockHeight,
				immature = tx.immature
			})
			.Where(o => o.id is not null)
			.ToArray();
			if (now is null)
				await Connection.ExecuteAsync("INSERT INTO txs(code, tx_id, raw, immature) VALUES (@code, @id, @raw, @immature) ON CONFLICT (code, tx_id) DO UPDATE SET raw = COALESCE(@raw, txs.raw), immature=EXCLUDED.immature", parameters);
			else
				await Connection.ExecuteAsync("INSERT INTO txs(code, tx_id, raw, immature, seen_at) VALUES (@code, @id, @raw, @immature, @seen_at) ON CONFLICT (code, tx_id) DO UPDATE SET seen_at=LEAST(@seen_at, txs.seen_at), raw = COALESCE(@raw, txs.raw), immature=EXCLUDED.immature", parameters);
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
