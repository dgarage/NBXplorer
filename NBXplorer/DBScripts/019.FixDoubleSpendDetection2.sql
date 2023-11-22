CREATE OR REPLACE FUNCTION blks_confirmed_update_txs() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
	r RECORD;
	maturity_height BIGINT;
BEGIN
  IF NEW.confirmed = OLD.confirmed THEN
	RETURN NEW;
  END IF;
  IF NEW.confirmed IS TRUE THEN
	-- TODO: We assume 100 blocks for immaturity. We should probably make this data configurable on separate table.
	maturity_height := (SELECT height - 100 + 1 FROM get_tip(NEW.code));
	-- Turn immature flag of outputs to mature
	-- Note that we never set the outputs back to immature, even in reorg
	-- But that's such a corner case that we don't care.
	WITH q AS (
	  SELECT t.code, tx_id FROM txs t
	  JOIN blks b ON b.code=t.code AND b.blk_id=t.blk_id
	  WHERE t.code=NEW.code AND t.immature IS TRUE AND b.height < maturity_height
	)
	UPDATE txs t SET immature='f' 
	FROM q
	WHERE t.code=q.code AND t.tx_id=q.tx_id;
	-- Turn mempool flag of confirmed txs to false
	WITH q AS (
	SELECT t.code, t.tx_id, bt.blk_id, bt.blk_idx, b.height FROM txs t
	JOIN blks_txs bt USING (code, tx_id)
	JOIN blks b ON b.code=t.code AND b.blk_id=bt.blk_id
	WHERE t.code=NEW.code AND bt.blk_id=NEW.blk_id)
	UPDATE txs t SET mempool='f', replaced_by=NULL, blk_id=q.blk_id, blk_idx=q.blk_idx, blk_height=q.height
	FROM q
	WHERE t.code=q.code AND t.tx_id=q.tx_id;
	-- Turn mempool flag of txs with inputs spent by confirmed blocks to false
	WITH q AS (
	SELECT mempool_ins.code, mempool_ins.tx_id mempool_tx_id, confirmed_ins.tx_id confirmed_tx_id
	FROM 
	  (SELECT i.code, i.spent_tx_id, i.spent_idx, t.tx_id FROM ins i
	  JOIN txs t USING (code, tx_id)
	  WHERE i.code=NEW.code AND t.mempool IS TRUE) mempool_ins
	LEFT JOIN (
	  SELECT i.code, i.spent_tx_id, i.spent_idx, t.tx_id FROM ins i
	  JOIN txs t USING (code, tx_id)
	  WHERE i.code=NEW.code AND t.blk_id = NEW.blk_id
	) confirmed_ins USING (code, spent_tx_id, spent_idx)
	WHERE confirmed_ins.tx_id IS NOT NULL) -- The use of LEFT JOIN is intentional, it forces postgres to use a specific index
	UPDATE txs t SET mempool='f', replaced_by=q.confirmed_tx_id
	FROM q
	WHERE t.code=q.code AND t.tx_id=q.mempool_tx_id;
  ELSE -- IF not confirmed anymore
	-- Set mempool flags of the txs in the blocks back to true
	WITH q AS (
	  SELECT code, tx_id FROM blks_txs
	  WHERE code=NEW.code AND blk_id=NEW.blk_id
	)
	-- We can't query over txs.blk_id directly, because it doesn't have an index
	UPDATE txs t
	SET mempool='t', blk_id=NULL, blk_idx=NULL, blk_height=NULL
	FROM q
	WHERE t.code=q.code AND t.tx_id = q.tx_id;
  END IF;
  -- Remove from spent_outs all outputs whose tx isn't in the mempool anymore
  DELETE FROM spent_outs so
  WHERE so.code = NEW.code
  AND NOT EXISTS (
    -- Returns true if any tx referenced by the spent_out is in the mempool
    SELECT 1 FROM txs
    WHERE code=so.code AND mempool IS TRUE AND tx_id = ANY(ARRAY[so.tx_id, so.spent_by, so.prev_spent_by]));
  RETURN NEW;
END
$$;