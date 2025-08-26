#nullable enable
using Dapper;
using NBitcoin;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NBXplorer.Backend
{
	public abstract record GetTransactionQuery
	{
		public static TrackedSourceTxId Create(TrackedSource TrackedSource, uint256? TxId = null, DateTimeOffset? from = null, DateTimeOffset? to = null) => new TrackedSourceTxId(TrackedSource, TxId, from, to);
		public static ScriptsTxIds Create(KeyPathInformation[] KeyInfos, uint256[] TxIds) => new ScriptsTxIds(KeyInfos, TxIds);
		public record TrackedSourceTxId(TrackedSource TrackedSource, uint256? TxId, DateTimeOffset? From, DateTimeOffset? To) : GetTransactionQuery
		{
			string? walletId;
			public override string GetSql(DynamicParameters parameters, NBXplorerNetwork network)
			{
				string txIdCond = String.Empty, fromCond = String.Empty, toCond = String.Empty;
				if (TxId is not null)
				{
					txIdCond = " AND tx_id=@tx_id";
					parameters.Add("@tx_id", TxId.ToString());
				}
				if (From is DateTimeOffset f)
				{
					fromCond = " AND @from <= seen_at";
					parameters.Add("@from", f);
				}
				if (To is DateTimeOffset t)
				{
					toCond = " AND seen_at <= @to";
					parameters.Add("@to", t);
				}
				walletId = Repository.GetWalletKey(TrackedSource, network).wid;
				parameters.Add("@walletId", walletId);
				parameters.Add("@code", network.CryptoCode);

				return $"""
				SELECT wallet_id, tx_id, idx, blk_id, blk_height, blk_idx, is_out, spent_tx_id, spent_idx, script, s.addr, value, asset_id, immature, keypath, key_idx, seen_at, feature
				FROM nbxv1_tracked_txs LEFT JOIN scripts s USING (code, script)
				WHERE code=@code AND wallet_id=@walletId{txIdCond}{fromCond}{toCond}
				""";
			}
			public override TrackedSource? GetTrackedSource(string wallet_id) => walletId == wallet_id ? TrackedSource : null;
		}

		public record ScriptsTxIds(KeyPathInformation[] KeyInfos, uint256[] TxIds) : GetTransactionQuery
		{
			Dictionary<string, TrackedSource> widToTrackedSource = new Dictionary<string, TrackedSource>();
			public override string GetSql(DynamicParameters parameters, NBXplorerNetwork network)
			{
				widToTrackedSource.Clear();
				foreach (var k in KeyInfos)
				{
					widToTrackedSource.TryAdd(Repository.GetWalletKey(k.TrackedSource, network).wid, k.TrackedSource);
				}
				parameters.Add("@code", network.CryptoCode);
				parameters.Add("@tx_ids", TxIds.Select(t => t.ToString()).ToArray());
				return """
				SELECT wallet_id, t.tx_id, idx, blk_id, blk_height, blk_idx, is_out, spent_tx_id, spent_idx, script, s.addr, value, asset_id, immature, keypath, key_idx, seen_at, feature
				FROM nbxv1_tracked_txs LEFT JOIN scripts s USING (code, script)
				JOIN unnest(@tx_ids) t(tx_id) USING (tx_id)
				WHERE code=@code
				""";
			}
			public override TrackedSource? GetTrackedSource(string wallet_id) => widToTrackedSource.TryGetValue(wallet_id, out var trackedSource) ? trackedSource : null;
		}

		public abstract string GetSql(DynamicParameters parameters, NBXplorerNetwork network);
		public abstract TrackedSource? GetTrackedSource(string wallet_id);
	}
}
