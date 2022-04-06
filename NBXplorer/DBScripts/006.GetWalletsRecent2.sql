-- Useful view to see what has going on recently in a wallet. Doesn't depends on wallets_history.
-- Better to use on Postgres 13+, as it greatly benefits from incremental sort feature.
CREATE OR REPLACE FUNCTION get_wallets_recent(in_wallet_id TEXT, in_code TEXT, in_interval INTERVAL, in_limit INT, in_offset INT)
RETURNS TABLE(code TEXT, asset_id TEXT, tx_id TEXT, seen_at TIMESTAMPTZ, balance_change BIGINT, balance_total BIGINT) AS $$
  -- We need to materialize, if too many utxos, postgres just call this one over and over...
  -- however Postgres 11 doesn't support AS MATERIALIZED :(
  WITH this_balances AS (
	  SELECT code, asset_id, unconfirmed_balance FROM wallets_balances
	  WHERE wallet_id=in_wallet_id
  ),
  latest_txs AS (
	SELECT  io.code,
			io.asset_id,
			blk_idx,
			blk_height,
			tx_id,
			seen_at,
			COALESCE(SUM (value) FILTER (WHERE is_out IS TRUE), 0) -  COALESCE(SUM (value) FILTER (WHERE is_out IS FALSE), 0) balance_change
		FROM ins_outs io
		JOIN wallets_scripts ws USING (code, script)
		WHERE ((CURRENT_TIMESTAMP - in_interval) <= seen_at) AND (in_code IS NULL OR in_code=io.code) AND (blk_id IS NOT NULL OR (mempool IS TRUE AND replaced_by IS NULL)) AND wallet_id=in_wallet_id
		GROUP BY io.code, io.asset_id, tx_id, seen_at, blk_height, blk_idx
		ORDER BY seen_at DESC, blk_height DESC, blk_idx DESC, asset_id
	LIMIT in_limit
  )
  SELECT q.code, q.asset_id, q.tx_id, q.seen_at, q.balance_change::BIGINT, (COALESCE((q.latest_balance - LAG(balance_change_sum, 1) OVER (PARTITION BY code, asset_id ORDER BY seen_at DESC, blk_height DESC, blk_idx DESC)), q.latest_balance))::BIGINT balance_total FROM
	  (SELECT q.*,
			  COALESCE((SELECT unconfirmed_balance FROM this_balances WHERE code=q.code AND asset_id=q.asset_id), 0) latest_balance,
			  SUM(q.balance_change) OVER (PARTITION BY code, asset_id ORDER BY seen_at DESC, blk_height DESC, blk_idx DESC) balance_change_sum FROM 
		  latest_txs q
	  ) q
  ORDER BY seen_at DESC, blk_height DESC, blk_idx DESC, asset_id
  OFFSET in_offset
$$ LANGUAGE SQL STABLE;

CREATE OR REPLACE FUNCTION get_wallets_recent(in_wallet_id TEXT, in_interval INTERVAL, in_limit INT, in_offset INT)
RETURNS TABLE(code TEXT, asset_id TEXT, tx_id TEXT, seen_at TIMESTAMPTZ, balance_change BIGINT, balance_total BIGINT) AS $$
  SELECT get_wallets_recent(in_wallet_id, NULL, in_interval, in_limit, in_offset)
$$ LANGUAGE SQL STABLE;