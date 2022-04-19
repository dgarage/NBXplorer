-- Disable triggers
SET session_replication_role = replica;

-- This script generate a whale wallet with 223000 transactions, used to check query performance.
INSERT INTO blks
SELECT 'BTC', 
	   encode(sha256(('b-' || s)::bytea), 'hex') blk_id,
	   s height,
	   encode(sha256(('b-' || (s-1))::bytea), 'hex') prev_id,
	   't'
FROM generate_series(0, 223000) s;

INSERT INTO txs
SELECT 'BTC',
		encode(sha256(('t-' || s)::bytea), 'hex') tx_id,
		NULL raw,
		NULL metadata,
		'f' immature,
		encode(sha256(('b-' || s)::bytea), 'hex') blk_id,
		0 blk_idx,
		s blk_height,
		'f' mempool,
		NULL,
		CURRENT_TIMESTAMP -  interval '1 minute' * (223000 - s) seen_at
FROM generate_series(0, 223000) s;

INSERT INTO blks_txs
SELECT 'BTC',
		encode(sha256(('b-' || s)::bytea), 'hex') blk_id,
		encode(sha256(('t-' || s)::bytea), 'hex') tx_id, 
		0 blk_idx
FROM generate_series(0, 223000) s;

INSERT INTO scripts
SELECT 'BTC',
		encode(sha256(('s-' || s)::bytea), 'hex') script,
		encode(sha256(('s-' || s)::bytea), 'hex') addr
FROM generate_series(0, 223000) s;


INSERT INTO outs
SELECT 'BTC',
		encode(sha256(('t-' || s)::bytea), 'hex') tx_id,
		0 idx,
		encode(sha256(('s-' || s)::bytea), 'hex') script,
		40 "value",
		'' asset_id,
		NULL /*CASE WHEN s != 223000 THEN encode(sha256(('t-' || s + 1)::bytea), 'hex') ELSE NULL END input_tx_id */,
		NULL /* CASE WHEN s != 223000 THEN 0 ELSE NULL END input_idx */,
		'f' input_mempool,
		'f' immature,
		encode(sha256(('b-' || s)::bytea), 'hex') blk_id,
		0 blk_idx,
		s blk_height,
		'f' mempool,
		NULL replaced_by,
		CURRENT_TIMESTAMP -  interval '1 minute' * (223000 - s) seen_at
FROM generate_series(0, 223000) s
WHERE MOD(s, 2) = 0;

INSERT INTO ins
SELECT 'BTC',
		encode(sha256(('t-' || s)::bytea), 'hex') tx_id,
		0 idx,
		encode(sha256(('t-' || (s-1))::bytea), 'hex') spent_tx_id,
		0 spent_idx,
		encode(sha256(('s-' || s-1)::bytea), 'hex') script,
		40 "value", 
		'' asset_id,
		encode(sha256(('b-' || s)::bytea), 'hex') blk_id,
		0 blk_idx,
		s blk_height,
		'f' mempool,
		NULL replaced_by,
		CURRENT_TIMESTAMP -  interval '1 minute' * (223000 - s) seen_at
FROM generate_series(0, 223000) s
WHERE MOD(s, 2) = 1;

UPDATE outs SET
  input_tx_id= (CASE WHEN blk_height != 223000 THEN encode(sha256(('t-' || blk_height + 1)::bytea), 'hex') ELSE NULL END),
  input_idx= (CASE WHEN blk_height != 223000 THEN 0 ELSE NULL END);

INSERT INTO ins_outs
  SELECT
	i.code,
	i.tx_id,
	i.idx,
	'f',
	i.spent_tx_id,
	i.spent_idx,
	i.script,
	i.value,
	i.asset_id,
	NULL,
	t.blk_id,
	t.blk_idx,
	t.blk_height,
	t.mempool,
	t.replaced_by,
	t.seen_at
	FROM ins i
	JOIN txs t USING (code, tx_id);

INSERT INTO ins_outs
  SELECT
	o.code,
	o.tx_id,
	o.idx,
	't',
	NULL,
	NULL,
	o.script,
	o.value,
	o.asset_id,
	o.immature,
	t.blk_id,
	t.blk_idx,
	t.blk_height,
	t.mempool,
	t.replaced_by,
	t.seen_at
	FROM outs o
	JOIN txs t ON t.code=o.code AND t.tx_id=o.tx_id;

INSERT INTO wallets VALUES ('WHALE');
INSERT INTO descriptors VALUES ('BTC', 'WHALEDESC');

INSERT INTO descriptors_scripts
SELECT 'BTC', 'WHALEDESC', s, encode(sha256(('s-' || s)::bytea), 'hex') script
FROM generate_series(0, 223000) s;

INSERT INTO wallets_scripts
SELECT 'BTC', encode(sha256(('s-' || s)::bytea), 'hex') script, 'WHALE'
FROM generate_series(0, 223000) s;

UPDATE descriptors_scripts
SET used='t'
WHERE idx < 223000 - 30;

ANALYZE;
SELECT wallets_history_refresh();
ANALYZE;

-- Re-enable triggers
SET session_replication_role = DEFAULT;