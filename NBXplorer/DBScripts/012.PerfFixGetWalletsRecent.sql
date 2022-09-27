CREATE OR REPLACE FUNCTION get_wallets_recent(in_wallet_id TEXT, in_code text, in_interval INTERVAL, in_limit INT, in_offset INT)
RETURNS TABLE(code TEXT, asset_id TEXT, tx_id TEXT, seen_at TIMESTAMPTZ, balance_change BIGINT, balance_total BIGINT) AS $$
  SELECT * FROM get_wallets_recent(in_wallet_id, in_code, NULL, in_interval, in_limit, in_offset)
$$ LANGUAGE SQL STABLE;

CREATE OR REPLACE FUNCTION get_wallets_recent(in_wallet_id TEXT, in_interval INTERVAL, in_limit INT, in_offset INT)
RETURNS TABLE(code TEXT, asset_id TEXT, tx_id TEXT, seen_at TIMESTAMPTZ, balance_change BIGINT, balance_total BIGINT) AS $$
  SELECT * FROM get_wallets_recent(in_wallet_id, NULL, NULL, in_interval, in_limit, in_offset)
$$ LANGUAGE SQL STABLE;
