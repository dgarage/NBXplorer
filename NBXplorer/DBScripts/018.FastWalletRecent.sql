CREATE OR REPLACE FUNCTION get_wallets_recent(in_wallet_id text, in_code text, in_asset_id text, in_interval interval, in_limit integer, in_offset integer) RETURNS TABLE(code text, asset_id text, tx_id text, seen_at timestamp with time zone, balance_change bigint, balance_total bigint)
    LANGUAGE sql STABLE
    AS $$
  WITH this_balances AS MATERIALIZED (
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
		WHERE ((CURRENT_TIMESTAMP - in_interval) <= seen_at) AND
		      (in_code IS NULL OR in_code=io.code) AND
			  (in_asset_id IS NULL OR in_asset_id=io.asset_id) AND
			  (blk_id IS NOT NULL OR (mempool IS TRUE AND replaced_by IS NULL)) AND
			  wallet_id=in_wallet_id
		GROUP BY io.code, io.asset_id, tx_id, seen_at, blk_height, blk_idx
		ORDER BY seen_at DESC, blk_height DESC, blk_idx DESC, asset_id
	LIMIT in_limit + in_offset
  )
  SELECT q.code, q.asset_id, q.tx_id, q.seen_at, q.balance_change::BIGINT, (COALESCE((q.latest_balance - LAG(balance_change_sum, 1) OVER (PARTITION BY code, asset_id ORDER BY seen_at DESC, blk_height DESC, blk_idx DESC)), q.latest_balance))::BIGINT balance_total FROM
	  (SELECT q.*,
			  COALESCE((SELECT unconfirmed_balance FROM this_balances WHERE code=q.code AND asset_id=q.asset_id), 0) latest_balance,
			  SUM(q.balance_change) OVER (PARTITION BY code, asset_id ORDER BY seen_at DESC, blk_height DESC, blk_idx DESC) balance_change_sum FROM 
		  latest_txs q
	  ) q
  ORDER BY seen_at DESC, blk_height DESC, blk_idx DESC, asset_id
  OFFSET in_offset
$$;