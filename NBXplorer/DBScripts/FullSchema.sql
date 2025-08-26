-- Generated with MaintenanceUtilities.GeneratedFullSchema

CREATE TYPE nbxv1_ds AS (
	descriptor text,
	idx bigint,
	script text,
	metadata jsonb,
	addr text,
	used boolean
);

CREATE TYPE new_in AS (
	tx_id text,
	idx bigint,
	spent_tx_id text,
	spent_idx bigint
);

CREATE TYPE new_out AS (
	tx_id text,
	idx bigint,
	script text,
	value bigint,
	asset_id text
);

CREATE TYPE outpoint AS (
	tx_id text,
	idx bigint
);

CREATE FUNCTION blks_confirmed_update_txs() RETURNS trigger
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

CREATE FUNCTION blks_txs_denormalize() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
	r RECORD;
BEGIN
	SELECT confirmed, height INTO r FROM blks WHERE code=NEW.code AND blk_id=NEW.blk_id;
	IF 
	  r.confirmed IS TRUE
	THEN
	  -- Propagate values to txs
	  UPDATE txs
	  SET blk_id=NEW.blk_id, blk_idx=NEW.blk_idx, blk_height=r.height, mempool='f', replaced_by=NULL
	  WHERE code=NEW.code AND tx_id=NEW.tx_id;
	END IF;
	RETURN NEW;
END
$$;

CREATE FUNCTION descriptors_scripts_after_insert_or_update_trigger_proc() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
  last_idx BIGINT;
BEGIN
  IF TG_OP='UPDATE' AND NEW.used IS NOT DISTINCT FROM OLD.used THEN
	RETURN NEW;
  END IF;
  IF TG_OP='INSERT' THEN
	-- Inherit the used flag of the script.
	NEW.used = (SELECT used FROM scripts WHERE code=NEW.code AND script=NEW.script);
	-- Bump next_idx if idx is greater or equal to it.
	-- Note that if the script is used, then the gap is now 0. But if not, the gap increased same value as the next_idx increase.
	UPDATE descriptors d
	  SET next_idx = NEW.idx + 1, gap=(CASE WHEN NEW.used THEN 0 ELSE d.gap + (NEW.idx + 1) - d.next_idx END)
	  WHERE code=NEW.code AND descriptor=NEW.descriptor AND next_idx < NEW.idx + 1;
	-- Early exit, we already updated the gap correctly. No need for some potentially expensive scan if used is false.
	IF FOUND THEN
	  RETURN NEW;
	END IF;
  END IF;
  -- Now we want to update the gap
  IF NEW.used THEN
	--  [1] [2] [3] [4] [5] then next_idx=6, imagine that 3 is now used, we want to update gap to be 2 (because we still have 2 addresses ahead)
	UPDATE descriptors d
	SET gap = next_idx - NEW.idx - 1 -- 6 - 3 - 1 = 2
	WHERE code=NEW.code AND descriptor=NEW.descriptor AND gap > next_idx - NEW.idx - 1; -- If an address has been used, the gap can't do up by definition
  ELSE -- If not used anymore, we need to scan descriptors_scripts to find the latest used.
	last_idx := (SELECT MAX(ds.idx) FROM descriptors_scripts ds WHERE ds.code=NEW.code AND ds.descriptor=NEW.descriptor AND ds.used IS TRUE AND ds.idx != NEW.idx);
	UPDATE descriptors d
	-- Say 1 and 3 was used. Then the newest latest used address will be 1 (last_idx) and gap should be 4 (gap = 6 - 1 - 1)
	SET gap = COALESCE(next_idx - last_idx - 1, next_idx)
	-- If the index was less than 1, then it couldn't have changed the gap... except if there is no last_idx
	WHERE code=NEW.code AND descriptor=NEW.descriptor  AND (last_idx IS NULL OR NEW.idx > last_idx); 
  END IF;
  RETURN NEW;
END $$;

CREATE FUNCTION descriptors_scripts_wallets_scripts_trigger_proc() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
  r RECORD;
BEGIN
  INSERT INTO wallets_scripts AS ws (code, script, wallet_id)
  SELECT ds.code, ds.script, wd.wallet_id FROM new_descriptors_scripts ds
  JOIN wallets_descriptors wd USING (code, descriptor)
  ON CONFLICT (code, script, wallet_id) DO UPDATE SET ref_count = ws.ref_count + 1;
  RETURN NULL;
END $$;

CREATE PROCEDURE fetch_matches(in_code text, in_outs public.new_out[], in_ins public.new_in[])
    LANGUAGE plpgsql
    AS $$
DECLARE
  has_match BOOLEAN;
BEGIN
  CALL fetch_matches(in_code, in_outs, in_ins, has_match);
END $$;

CREATE PROCEDURE fetch_matches(in_code text, in_outs public.new_out[], in_ins public.new_in[], INOUT has_match boolean)
    LANGUAGE plpgsql
    AS $$
BEGIN
	BEGIN
	  TRUNCATE TABLE matched_outs, matched_ins, matched_conflicts, new_ins;
	EXCEPTION WHEN others THEN
	  CREATE TEMPORARY TABLE IF NOT EXISTS matched_outs (LIKE new_out);
	  ALTER TABLE matched_outs ADD COLUMN IF NOT EXISTS "order" BIGINT;
	  CREATE TEMPORARY TABLE IF NOT EXISTS new_ins (LIKE new_in);
	  ALTER TABLE new_ins ADD COLUMN IF NOT EXISTS "order" BIGINT;
	  ALTER TABLE new_ins ADD COLUMN IF NOT EXISTS code TEXT;
	  CREATE TEMPORARY TABLE IF NOT EXISTS matched_ins (LIKE new_ins);
	  ALTER TABLE matched_ins ADD COLUMN IF NOT EXISTS script TEXT;
	  ALTER TABLE matched_ins ADD COLUMN IF NOT EXISTS value bigint;
	  ALTER TABLE matched_ins ADD COLUMN IF NOT EXISTS asset_id TEXT;
	  CREATE TEMPORARY TABLE IF NOT EXISTS matched_conflicts (
		code TEXT,
		spent_tx_id TEXT,
		spent_idx BIGINT,
		replacing_tx_id TEXT,
		replaced_tx_id TEXT,
		is_new BOOLEAN);
	END;
	has_match := 'f';
	INSERT INTO matched_outs
	SELECT o.* FROM scripts s
	JOIN unnest(in_outs)  WITH ORDINALITY AS o(tx_id, idx, script, value, asset_id, "order") USING (script)
	WHERE s.code=in_code
	ORDER BY "order";
	-- Fancy way to remove dups (https://stackoverflow.com/questions/6583916/delete-duplicate-rows-from-small-table)
	DELETE FROM matched_outs a USING (
      SELECT MIN(ctid) as ctid, tx_id, idx
        FROM matched_outs
        GROUP BY tx_id, idx HAVING COUNT(*) > 1
      ) b
      WHERE a.tx_id = b.tx_id AND a.idx = b.idx
      AND a.ctid <> b.ctid;
	-- This table will include only the ins we need to add to the spent_outs for double spend detection
	INSERT INTO new_ins
	SELECT i.*, in_code code FROM unnest(in_ins) WITH ORDINALITY AS i(tx_id, idx, spent_tx_id, spent_idx, "order");
	INSERT INTO matched_ins
	SELECT * FROM
	  (SELECT i.*, o.script, o.value, o.asset_id  FROM new_ins i
	  JOIN outs o ON o.code=i.code AND o.tx_id=i.spent_tx_id AND o.idx=i.spent_idx
	  UNION ALL
	  SELECT i.*, o.script, o.value, o.asset_id  FROM new_ins i
	  JOIN matched_outs o ON i.spent_tx_id = o.tx_id AND i.spent_idx = o.idx) i
	ORDER BY "order";
	DELETE FROM new_ins
	WHERE NOT tx_id=ANY(SELECT tx_id FROM matched_ins UNION SELECT tx_id FROM matched_outs)
	AND NOT (spent_tx_id || spent_idx::TEXT)=ANY(SELECT (tx_id || idx::TEXT) FROM spent_outs);
	INSERT INTO matched_conflicts
	WITH RECURSIVE cte(code, spent_tx_id, spent_idx, replacing_tx_id, replaced_tx_id) AS
	(
	  SELECT 
		in_code code,
		i.spent_tx_id,
		i.spent_idx,
		i.tx_id replacing_tx_id,
		CASE
			WHEN so.spent_by != i.tx_id THEN so.spent_by
			ELSE so.prev_spent_by
		END replaced_tx_id,
		so.spent_by != i.tx_id is_new
	  FROM new_ins i
	  JOIN spent_outs so ON so.code=in_code AND so.tx_id=i.spent_tx_id AND so.idx=i.spent_idx
	  JOIN txs rt ON so.code=rt.code AND rt.tx_id=so.spent_by
	  WHERE rt.code=in_code AND rt.mempool IS TRUE
	  UNION
	  SELECT c.code, c.spent_tx_id, c.spent_idx, c.replacing_tx_id, i.tx_id replaced_tx_id, c.is_new FROM cte c
	  JOIN outs o ON o.code=c.code AND o.tx_id=c.replaced_tx_id
	  JOIN ins i ON i.code=c.code AND i.spent_tx_id=o.tx_id AND i.spent_idx=o.idx
	  WHERE i.code=c.code AND i.mempool IS TRUE
	)
	SELECT * FROM cte;
	DELETE FROM matched_ins a USING (
      SELECT MIN(ctid) as ctid, tx_id, idx
        FROM matched_ins
        GROUP BY tx_id, idx HAVING COUNT(*) > 1
      ) b
      WHERE a.tx_id = b.tx_id AND a.idx = b.idx
      AND a.ctid <> b.ctid;
	DELETE FROM matched_conflicts a USING (
      SELECT MIN(ctid) as ctid, replaced_tx_id
        FROM matched_conflicts
        GROUP BY replaced_tx_id HAVING COUNT(*) > 1
      ) b
      WHERE a.replaced_tx_id = b.replaced_tx_id
      AND a.ctid <> b.ctid;
	-- Make order start by 0, as most languages have array starting by 0
	UPDATE matched_ins i
	SET "order"=i."order" - 1;
	IF FOUND THEN
	  has_match := 't';
	END IF;
	UPDATE matched_outs o
	SET "order"=o."order" - 1;
	IF FOUND THEN
	  has_match := 't';
	END IF;
	PERFORM 1 FROM matched_conflicts WHERE is_new IS TRUE LIMIT 1;
	IF FOUND THEN
	  has_match := 't';
	END IF;
END $$;

CREATE FUNCTION generate_series_fixed(in_from timestamp with time zone, in_to timestamp with time zone, in_interval interval) RETURNS TABLE(s timestamp with time zone)
    LANGUAGE sql STABLE
    AS $$
  SELECT generate_series(in_from, in_to, in_interval)
  LIMIT  (EXTRACT(EPOCH FROM (in_to - in_from))/EXTRACT(EPOCH FROM in_interval)) + 1; -- I am unsure about the exact formula, but over estimating 1 row is fine...
$$;

CREATE FUNCTION get_tip(in_code text) RETURNS TABLE(code text, blk_id text, height bigint, prev_id text)
    LANGUAGE sql STABLE
    AS $$
  SELECT code, blk_id, height, prev_id FROM blks WHERE code=in_code AND confirmed IS TRUE ORDER BY height DESC LIMIT 1
$$;

CREATE FUNCTION get_wallets_histogram(in_wallet_id text, in_code text, in_asset_id text, in_from timestamp with time zone, in_to timestamp with time zone, in_interval interval) RETURNS TABLE(date timestamp with time zone, balance_change bigint, balance bigint)
    LANGUAGE sql STABLE
    AS $$
  SELECT s AS time,
  		change::bigint,
  		(SUM (q.change) OVER (ORDER BY s) + COALESCE((SELECT balance_total FROM wallets_history WHERE seen_at < in_from AND wallet_id=in_wallet_id AND code=in_code AND asset_id=in_asset_id ORDER BY seen_at DESC, blk_height DESC, blk_idx DESC LIMIT 1), 0))::BIGINT  AS balance
  FROM generate_series_fixed(in_from, in_to - in_interval, in_interval) s
  LEFT JOIN LATERAL (
	  SELECT s, COALESCE(SUM(balance_change),0) change FROM wallets_history
	  WHERE  s <= seen_at AND seen_at < s + in_interval AND wallet_id=in_wallet_id AND code=in_code AND asset_id=in_asset_id
  ) q USING (s)
$$;

CREATE FUNCTION get_wallets_recent(in_wallet_id text, in_interval interval, in_limit integer, in_offset integer) RETURNS TABLE(code text, asset_id text, tx_id text, seen_at timestamp with time zone, balance_change bigint, balance_total bigint)
    LANGUAGE sql STABLE
    AS $$
  SELECT * FROM get_wallets_recent(in_wallet_id, NULL, NULL, in_interval, in_limit, in_offset)
$$;

CREATE FUNCTION get_wallets_recent(in_wallet_id text, in_code text, in_interval interval, in_limit integer, in_offset integer) RETURNS TABLE(code text, asset_id text, tx_id text, seen_at timestamp with time zone, balance_change bigint, balance_total bigint)
    LANGUAGE sql STABLE
    AS $$
  SELECT * FROM get_wallets_recent(in_wallet_id, in_code, NULL, in_interval, in_limit, in_offset)
$$;

CREATE FUNCTION get_wallets_recent(in_wallet_id text, in_code text, in_asset_id text, in_interval interval, in_limit integer, in_offset integer) RETURNS TABLE(code text, asset_id text, tx_id text, seen_at timestamp with time zone, balance_change bigint, balance_total bigint)
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

CREATE FUNCTION ins_after_insert2_trigger_proc() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
  r RECORD;
BEGIN
  IF NEW.blk_id IS NOT NULL OR (NEW.mempool IS TRUE AND NEW.replaced_by IS NULL)  THEN
	UPDATE outs SET input_tx_id=NEW.tx_id, input_idx=NEW.idx, input_mempool=NEW.mempool
	WHERE (code=NEW.code AND tx_id=NEW.spent_tx_id AND idx=NEW.spent_idx);
  END IF;
  RETURN NEW;
END
$$;

CREATE FUNCTION ins_after_insert_trigger_proc() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
  r RECORD;
BEGIN
  -- Duplicate the ins into the ins_outs table
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
	FROM new_ins i
	JOIN txs t USING (code, tx_id);
  RETURN NULL;
END
$$;

CREATE FUNCTION ins_after_update_trigger_proc() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
  r RECORD;
BEGIN
  -- Just (un)confirmed? Update the out's spent_by
  IF NEW.blk_id IS DISTINCT FROM OLD.blk_id THEN
	  UPDATE outs SET input_tx_id=NEW.tx_id, input_idx=NEW.idx, input_mempool=NEW.mempool
	  WHERE (code=NEW.code AND tx_id=NEW.spent_tx_id AND idx=NEW.spent_idx);
  END IF;
  -- Kicked off mempool? If it's replaced or not in blk anymore, update outs spent_by
  IF (NEW.mempool IS FALSE AND OLD.mempool IS TRUE) AND (NEW.replaced_by IS NOT NULL OR NEW.blk_id IS NULL) THEN
	  UPDATE outs SET input_tx_id=NULL, input_idx=NULL, input_mempool='f'
	  WHERE (code=NEW.code AND tx_id=NEW.spent_tx_id AND idx=NEW.spent_idx) AND (input_tx_id=NEW.tx_id AND input_idx=NEW.idx);
  END IF;
  RETURN NEW;
END
$$;

CREATE FUNCTION ins_before_insert_trigger_proc() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
  r RECORD;
BEGIN
   -- Take the denormalized values from the associated tx, and spent outs, put them in the inserted
  SELECT * INTO r FROM txs WHERE code=NEW.code AND tx_id=NEW.tx_id;
  NEW.blk_id = r.blk_id;
  NEW.blk_id = r.blk_id;
  NEW.mempool = r.mempool;
  NEW.replaced_by = r.replaced_by;
  NEW.seen_at = r.seen_at;
  SELECT * INTO r FROM outs WHERE code=NEW.code AND tx_id=NEW.spent_tx_id AND idx=NEW.spent_idx;
  IF NOT FOUND THEN
	RETURN NULL;
  END IF;
  NEW.script = r.script;
  NEW.value = r.value;
  NEW.asset_id = r.asset_id;
  RETURN NEW;
END
$$;

CREATE FUNCTION ins_delete_ins_outs() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  DELETE FROM ins_outs io WHERE io.code=OLD.code AND io.tx_id=OLD.tx_id AND io.idx=OLD.idx AND io.is_out IS FALSE;
  RETURN OLD;
END
$$;

CREATE FUNCTION nbxv1_get_descriptor_id(in_code text, in_scheme text, in_feature text) RETURNS text
    LANGUAGE sql IMMUTABLE
    AS $$
	   SELECT encode(substring(sha256((in_code || '|' || in_scheme || '|' || in_feature)::bytea), 0, 22), 'base64')
$$;

CREATE FUNCTION nbxv1_get_keypath(metadata jsonb, idx bigint) RETURNS text
    LANGUAGE sql IMMUTABLE
    AS $$
	   SELECT REPLACE(metadata->>'keyPathTemplate', '*', idx::TEXT)
$$;

CREATE FUNCTION nbxv1_get_keypath_index(metadata jsonb, keypath text) RETURNS bigint
    LANGUAGE sql IMMUTABLE
    AS $_$
  SELECT
  CASE WHEN keypath LIKE (prefix || '%') AND 
            keypath LIKE ('%' || suffix) AND
            idx ~ '^\d+$'
       THEN CAST(idx AS BIGINT) END
  FROM (SELECT SUBSTRING(
              keypath
              FROM LENGTH(prefix) + 1
              FOR LENGTH(keypath) - LENGTH(prefix) - LENGTH(suffix)
          ) idx, prefix, suffix
      FROM (
      SELECT
          split_part(metadata->>'keyPathTemplate', '*', 1) AS prefix,
          split_part(metadata->>'keyPathTemplate', '*', 2) AS suffix
      ) parts) q;
$_$;

CREATE FUNCTION nbxv1_get_wallet_id(in_code text, in_scheme_or_address text) RETURNS text
    LANGUAGE sql IMMUTABLE
    AS $$
	   SELECT encode(substring(sha256((in_code || '|' || in_scheme_or_address)::bytea), 0, 22), 'base64')
$$;

CREATE FUNCTION outs_after_update_trigger_proc() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  IF NEW.asset_id IS DISTINCT FROM OLD.asset_id OR NEW.value IS DISTINCT FROM OLD.value THEN
	WITH cte AS (
	UPDATE ins SET asset_id=NEW.asset_id, value=NEW.value
	WHERE code=NEW.code AND spent_tx_id=NEW.tx_id AND spent_idx=NEW.idx
	RETURNING code, tx_id, idx)
	UPDATE ins_outs io SET asset_id=NEW.asset_id, value=NEW.value
	FROM cte
	WHERE cte.code=io.code AND cte.tx_id=io.tx_id AND cte.idx=io.idx AND is_out IS FALSE;
	UPDATE ins_outs SET asset_id=NEW.asset_id, value=NEW.value
	WHERE code=NEW.code AND tx_id=NEW.tx_id AND idx=NEW.idx AND is_out IS TRUE;
  END IF;
  RETURN NULL;
END
$$;

CREATE FUNCTION outs_delete_ins_outs() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  DELETE FROM ins_outs io WHERE io.code=OLD.code AND io.tx_id=OLD.tx_id AND io.idx=OLD.idx AND io.is_out IS TRUE;
  DELETE FROM ins_outs io WHERE io.code=OLD.code AND io.spent_tx_id=OLD.tx_id AND io.spent_idx=OLD.idx AND io.is_out IS FALSE;
  RETURN OLD;
END
$$;

CREATE FUNCTION outs_denormalize_from_tx() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
  r RECORD;
BEGIN
  SELECT * INTO r FROM txs WHERE code=NEW.code AND tx_id=NEW.tx_id;
  IF r IS NULL THEN
	RETURN NEW; -- This will crash on foreign key constraint
  END IF;
  NEW.immature = r.immature;
  NEW.blk_id = r.blk_id;
  NEW.blk_idx = r.blk_idx;
  NEW.blk_height = r.blk_height;
  NEW.mempool = r.mempool;
  NEW.replaced_by = r.replaced_by;
  NEW.seen_at = r.seen_at;
  RETURN NEW;
END
$$;

CREATE FUNCTION outs_denormalize_to_ins_outs() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
  r RECORD;
BEGIN
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
	FROM new_outs o
	JOIN txs t ON t.code=o.code AND t.tx_id=o.tx_id;
	-- Mark scripts as used
	FOR r IN SELECT * FROM new_outs
	LOOP
	  UPDATE scripts
		SET used='t'
		WHERE code=r.code AND script=r.script AND used IS FALSE;
	END LOOP;
	RETURN NULL;
END
$$;

CREATE PROCEDURE save_matches(in_code text)
    LANGUAGE plpgsql
    AS $$
BEGIN
  CALL save_matches (in_code, CURRENT_TIMESTAMP);
END $$;

CREATE PROCEDURE save_matches(in_code text, in_seen_at timestamp with time zone)
    LANGUAGE plpgsql
    AS $$
DECLARE
  r RECORD;
BEGIN
  
  INSERT INTO txs (code, tx_id, seen_at)
  SELECT in_code, q.tx_id, in_seen_at FROM
  (
	SELECT tx_id FROM matched_outs
	UNION
	SELECT tx_id FROM matched_ins
    UNION
    SELECT replacing_tx_id FROM matched_conflicts
  ) q
  ON CONFLICT (code, tx_id)
  DO UPDATE SET seen_at=in_seen_at
  WHERE in_seen_at < txs.seen_at;
  INSERT INTO outs (code, tx_id, idx, script, value, asset_id)
  SELECT in_code, tx_id, idx, script, value, asset_id
  FROM matched_outs
  ON CONFLICT DO NOTHING;
  INSERT INTO ins (code, tx_id, idx, spent_tx_id, spent_idx)
  SELECT in_code, tx_id, idx, spent_tx_id, spent_idx
  FROM matched_ins
  ON CONFLICT DO NOTHING;
  INSERT INTO spent_outs
  SELECT in_code, spent_tx_id, spent_idx, tx_id FROM new_ins
  ON CONFLICT DO NOTHING;
  FOR r IN
	SELECT * FROM matched_conflicts WHERE is_new IS TRUE
  LOOP
	UPDATE spent_outs SET spent_by=r.replacing_tx_id, prev_spent_by=r.replaced_tx_id
	WHERE code=r.code AND tx_id=r.spent_tx_id AND idx=r.spent_idx;
	UPDATE txs SET replaced_by=r.replacing_tx_id
	WHERE code=r.code AND tx_id=r.replaced_tx_id;
  END LOOP;
END $$;

CREATE PROCEDURE save_matches(in_code text, in_outs public.new_out[], in_ins public.new_in[])
    LANGUAGE plpgsql
    AS $$
BEGIN
  CALL save_matches (in_code, in_outs, in_ins, CURRENT_TIMESTAMP);
END $$;

CREATE PROCEDURE save_matches(in_code text, in_outs public.new_out[], in_ins public.new_in[], in_seen_at timestamp with time zone)
    LANGUAGE plpgsql
    AS $$
BEGIN
  CALL fetch_matches (in_code, in_outs, in_ins);
  CALL save_matches(in_code, in_seen_at);
END $$;

CREATE FUNCTION scripts_set_descriptors_scripts_used() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  IF NEW.used != OLD.used AND NEW.used IS TRUE THEN
    UPDATE descriptors_scripts ds SET used='t' WHERE code=NEW.code AND script=NEW.script AND used='f';
  END IF;
  RETURN NEW;
END $$;

CREATE FUNCTION to_btc(v bigint) RETURNS numeric
    LANGUAGE sql IMMUTABLE
    AS $$
	   SELECT ROUND(v::NUMERIC / 100000000, 8)
$$;

CREATE FUNCTION to_btc(v numeric) RETURNS numeric
    LANGUAGE sql IMMUTABLE
    AS $$
	   SELECT ROUND(v / 100000000, 8)
$$;

CREATE FUNCTION txs_denormalize() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
	r RECORD;
BEGIN
  -- Propagate any change to table outs, ins, and ins_outs
	UPDATE outs o SET  immature=NEW.immature, blk_id = NEW.blk_id, blk_idx = NEW.blk_idx, blk_height = NEW.blk_height, mempool = NEW.mempool, replaced_by = NEW.replaced_by, seen_at = NEW.seen_at
	WHERE o.code=NEW.code AND o.tx_id=NEW.tx_id;
	UPDATE ins i SET  blk_id = NEW.blk_id, blk_idx = NEW.blk_idx, blk_height = NEW.blk_height, mempool = NEW.mempool, replaced_by = NEW.replaced_by, seen_at = NEW.seen_at
	WHERE i.code=NEW.code AND i.tx_id=NEW.tx_id;
	UPDATE ins_outs io SET  immature=NEW.immature, blk_id = NEW.blk_id, blk_idx = NEW.blk_idx, blk_height = NEW.blk_height, mempool = NEW.mempool, replaced_by = NEW.replaced_by, seen_at = NEW.seen_at
	WHERE io.code=NEW.code AND io.tx_id=NEW.tx_id;
	-- Propagate any replaced_by / mempool to ins/outs/ins_outs and to the children
	IF NEW.replaced_by IS DISTINCT FROM OLD.replaced_by THEN
	  FOR r IN 
	  	SELECT code, tx_id, replaced_by FROM ins
		WHERE code=NEW.code AND spent_tx_id=NEW.tx_id AND replaced_by IS DISTINCT FROM NEW.replaced_by
	  LOOP
		UPDATE txs SET replaced_by=NEW.replaced_by
		WHERE code=r.code AND tx_id=r.tx_id;
	  END LOOP;
	END IF;
	IF NEW.mempool != OLD.mempool AND (NEW.mempool IS TRUE OR NEW.blk_id IS NULL) THEN
	  FOR r IN 
	  	SELECT code, tx_id, mempool FROM ins
		WHERE code=NEW.code AND spent_tx_id=NEW.tx_id AND mempool != NEW.mempool
	  LOOP
		UPDATE txs SET mempool=NEW.mempool
		WHERE code=r.code AND tx_id=r.tx_id;
	  END LOOP;
	END IF;
	RETURN NEW;
END
$$;

CREATE FUNCTION wallets_descriptors_after_delete_trigger_proc() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  WITH cte AS (
	SELECT ds.code, ds.script, wd.wallet_id FROM new_wallets_descriptors wd
	JOIN descriptors_scripts ds USING (code, descriptor)
  )
  UPDATE wallets_scripts ws
  SET ref_count = ws.ref_count - 1
  FROM cte
  WHERE cte.code=ws.code AND cte.script=ws.script AND cte.wallet_id=ws.wallet_id;
  RETURN NULL;
END;
$$;

CREATE FUNCTION wallets_descriptors_after_insert_trigger_proc() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  INSERT INTO wallets_scripts AS ws (code, script, wallet_id)
	  SELECT ds.code, ds.script, wd.wallet_id FROM new_wallets_descriptors wd
	  JOIN descriptors_scripts ds USING (code, descriptor)
  ON CONFLICT (code, script, wallet_id) DO UPDATE SET ref_count = ws.ref_count + 1;
  RETURN NULL;
END;
$$;

CREATE FUNCTION wallets_history_refresh() RETURNS boolean
    LANGUAGE plpgsql
    AS $$
DECLARE
  last_ins_outs TIMESTAMPTZ;
  last_wallets_history TIMESTAMPTZ;
BEGIN
   IF pg_try_advisory_xact_lock(75639) IS FALSE THEN
	RETURN FALSE;
   END IF;
   last_ins_outs := (SELECT max(seen_at) FROM ins_outs WHERE blk_id IS NOT NULL);
   last_wallets_history := (SELECT max(seen_at) FROM wallets_history);
   IF last_wallets_history IS DISTINCT FROM last_ins_outs THEN
	REFRESH MATERIALIZED VIEW CONCURRENTLY wallets_history;
	RETURN TRUE;
   END IF;
   RETURN FALSE;
EXCEPTION WHEN OTHERS THEN
  RETURN FALSE;
END
$$;

CREATE FUNCTION wallets_scripts_after_delete_trigger_proc() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  IF (SELECT COUNT(*) FROM new_wallets_scripts) = 0 THEN
  	RETURN NULL;
  END IF;
  WITH cte AS (
	SELECT ww.parent_id, nws.code, nws.script FROM new_wallets_scripts nws
	JOIN wallets_wallets ww ON ww.wallet_id=nws.wallet_id
	JOIN wallets_scripts ws ON ws.code=nws.code AND ws.script=nws.script AND ws.wallet_id=ww.parent_id
  )
  UPDATE wallets_scripts ws
  SET ref_count = ws.ref_count -1
  FROM cte
  WHERE cte.code=ws.code AND cte.script=ws.script AND cte.parent_id=ws.wallet_id;
  RETURN NULL;
END;
$$;

CREATE FUNCTION wallets_scripts_after_insert_trigger_proc() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  IF (SELECT COUNT(*) FROM new_wallets_scripts) = 0 THEN
  	RETURN NULL;
  END IF;
  INSERT INTO wallets_scripts AS ws (code, script, wallet_id)
  SELECT nws.code, nws.script, ww.parent_id FROM new_wallets_scripts nws
  JOIN wallets_wallets ww ON ww.wallet_id=nws.wallet_id
  ON CONFLICT (code, script, wallet_id) DO UPDATE SET ref_count = ws.ref_count + 1;
  RETURN NULL;
END;
$$;

CREATE FUNCTION wallets_scripts_after_update_trigger_proc() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  WITH cte AS (
	SELECT code, script, wallet_id FROM new_wallets_scripts
	WHERE ref_count <= 0
  )
  DELETE FROM wallets_scripts AS ws
  USING cte c
  WHERE c.code=ws.code AND c.script=ws.script AND c.wallet_id=ws.wallet_id;
  RETURN NULL;
END;
$$;

CREATE FUNCTION wallets_wallets_after_delete_trigger_proc() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
	WITH cte AS (
	  SELECT pws.code, pws.script, pws.wallet_id FROM new_wallets_wallets ww
	  JOIN wallets_scripts cws ON cws.wallet_id=ww.wallet_id
	  JOIN wallets_scripts pws ON pws.wallet_id=ww.parent_id AND cws.code=pws.code AND cws.script=pws.script
	)
	UPDATE wallets_scripts ws
	SET ref_count = ws.ref_count - 1
	FROM cte c
	WHERE c.code=ws.code AND c.script=ws.script AND c.wallet_id=ws.wallet_id;
  RETURN NULL;
END;
$$;

CREATE FUNCTION wallets_wallets_after_insert_trigger_proc() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  INSERT INTO wallets_scripts AS ws (code, script, wallet_id)
  SELECT ws.code, ws.script, ww.parent_id FROM new_wallets_wallets ww
  JOIN wallets_scripts ws ON ws.wallet_id=ww.wallet_id
  ON CONFLICT (code, script, wallet_id) DO UPDATE SET ref_count = ws.ref_count + 1;
  RETURN NULL;
END;
$$;

CREATE FUNCTION wallets_wallets_before_insert_trigger_proc() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
	r RECORD;
BEGIN
	FOR r IN 
	  WITH RECURSIVE cte (wallet_id, parent_id, path, has_cycle) AS (
	  SELECT NEW.wallet_id, NEW.parent_id, ARRAY[NEW.parent_id]::TEXT[], NEW.wallet_id IS NOT DISTINCT FROM NEW.parent_id
	  UNION ALL
	  SELECT cte.parent_id, ww.wallet_id, cte.path || ww.wallet_id,  ww.wallet_id=ANY(cte.path) FROM cte
	  JOIN wallets_wallets ww ON ww.parent_id=cte.wallet_id
	  WHERE has_cycle IS FALSE)
	  SELECT 1 FROM cte WHERE has_cycle IS TRUE
	LOOP
	  RAISE EXCEPTION 'Cycle detected';
	END LOOP;
	RETURN NEW;
END;
$$;

CREATE TABLE blks (
    code text NOT NULL,
    blk_id text NOT NULL,
    height bigint,
    prev_id text,
    confirmed boolean DEFAULT false,
    indexed_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE blks_txs (
    code text NOT NULL,
    blk_id text NOT NULL,
    tx_id text NOT NULL,
    blk_idx integer
);

CREATE TABLE descriptors (
    code text NOT NULL,
    descriptor text NOT NULL,
    metadata jsonb,
    next_idx bigint DEFAULT 0,
    gap bigint DEFAULT 0
);

CREATE TABLE descriptors_scripts (
    code text NOT NULL,
    descriptor text NOT NULL,
    idx bigint NOT NULL,
    script text NOT NULL,
    metadata jsonb,
    used boolean DEFAULT false NOT NULL
);

CREATE TABLE scripts (
    code text NOT NULL,
    script text NOT NULL,
    addr text NOT NULL,
    used boolean DEFAULT false NOT NULL
);

CREATE VIEW descriptors_scripts_unused AS
 SELECT ds.code,
    ds.descriptor,
    ds.script,
    ds.idx,
    s.addr,
    d.metadata AS d_metadata,
    ds.metadata AS ds_metadata
   FROM ((descriptors_scripts ds
     JOIN scripts s USING (code, script))
     JOIN descriptors d USING (code, descriptor))
  WHERE (ds.used IS FALSE);

CREATE TABLE ins (
    code text NOT NULL,
    tx_id text NOT NULL,
    idx bigint NOT NULL,
    spent_tx_id text NOT NULL,
    spent_idx bigint NOT NULL,
    script text NOT NULL,
    value bigint NOT NULL,
    asset_id text NOT NULL,
    blk_id text,
    blk_idx integer,
    blk_height bigint,
    mempool boolean DEFAULT true,
    replaced_by text,
    seen_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE ins_outs (
    code text NOT NULL,
    tx_id text NOT NULL,
    idx bigint NOT NULL,
    is_out boolean NOT NULL,
    spent_tx_id text,
    spent_idx bigint,
    script text NOT NULL,
    value bigint NOT NULL,
    asset_id text NOT NULL,
    immature boolean,
    blk_id text,
    blk_idx integer,
    blk_height bigint,
    mempool boolean DEFAULT true,
    replaced_by text,
    seen_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE nbxv1_evts (
    code text NOT NULL,
    id bigint NOT NULL,
    type text NOT NULL,
    data jsonb NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE nbxv1_evts_ids (
    code text NOT NULL,
    curr_id bigint
);

CREATE TABLE wallets_descriptors (
    code text NOT NULL,
    descriptor text NOT NULL,
    wallet_id text NOT NULL
);

CREATE TABLE wallets_scripts (
    code text NOT NULL,
    script text NOT NULL,
    wallet_id text NOT NULL,
    ref_count integer DEFAULT 1
);

CREATE VIEW nbxv1_keypath_info AS
 SELECT ws.code,
    ws.script,
    s.addr,
    d.metadata AS descriptor_metadata,
    nbxv1_get_keypath(d.metadata, ds.idx) AS keypath,
    ds.metadata AS descriptors_scripts_metadata,
    ws.wallet_id,
    ds.idx,
    ds.used,
    d.descriptor
   FROM ((wallets_scripts ws
     JOIN scripts s ON (((s.code = ws.code) AND (s.script = ws.script))))
     LEFT JOIN ((wallets_descriptors wd
     JOIN descriptors_scripts ds ON (((ds.code = wd.code) AND (ds.descriptor = wd.descriptor))))
     JOIN descriptors d ON (((d.code = ds.code) AND (d.descriptor = ds.descriptor)))) ON (((wd.wallet_id = ws.wallet_id) AND (wd.code = ws.code) AND (ds.script = ws.script))));

CREATE TABLE nbxv1_metadata (
    wallet_id text NOT NULL,
    key text NOT NULL,
    data jsonb
);

CREATE TABLE nbxv1_settings (
    code text NOT NULL,
    key text NOT NULL,
    data_bytes bytea,
    data_json jsonb
);

CREATE VIEW nbxv1_tracked_txs AS
 SELECT ws.wallet_id,
    io.code,
    io.tx_id,
    io.idx,
    io.is_out,
    io.spent_tx_id,
    io.spent_idx,
    io.script,
    io.value,
    io.asset_id,
    io.immature,
    io.blk_id,
    io.blk_idx,
    io.blk_height,
    io.mempool,
    io.replaced_by,
    io.seen_at,
    nbxv1_get_keypath(d.metadata, ds.idx) AS keypath,
    (d.metadata ->> 'feature'::text) AS feature,
    d.metadata AS descriptor_metadata,
    ds.idx AS key_idx
   FROM ((wallets_scripts ws
     JOIN ins_outs io ON (((io.code = ws.code) AND (io.script = ws.script))))
     LEFT JOIN ((wallets_descriptors wd
     JOIN descriptors_scripts ds ON (((ds.code = wd.code) AND (ds.descriptor = wd.descriptor))))
     JOIN descriptors d ON (((d.code = ds.code) AND (d.descriptor = ds.descriptor)))) ON (((wd.wallet_id = ws.wallet_id) AND (wd.code = ws.code) AND (ds.script = ws.script))))
  WHERE ((io.blk_id IS NOT NULL) OR (io.mempool IS TRUE));

CREATE TABLE outs (
    code text NOT NULL,
    tx_id text NOT NULL,
    idx bigint NOT NULL,
    script text NOT NULL,
    value bigint NOT NULL,
    asset_id text DEFAULT ''::text NOT NULL,
    input_tx_id text,
    input_idx bigint,
    input_mempool boolean DEFAULT false NOT NULL,
    immature boolean DEFAULT false NOT NULL,
    blk_id text,
    blk_idx integer,
    blk_height bigint,
    mempool boolean DEFAULT true,
    replaced_by text,
    seen_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE spent_outs (
    code text NOT NULL,
    tx_id text NOT NULL,
    idx bigint NOT NULL,
    spent_by text NOT NULL,
    prev_spent_by text,
    spent_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE txs (
    code text NOT NULL,
    tx_id text NOT NULL,
    raw bytea,
    metadata jsonb,
    immature boolean DEFAULT false,
    blk_id text,
    blk_idx integer,
    blk_height bigint,
    mempool boolean DEFAULT true,
    replaced_by text,
    seen_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE VIEW utxos AS
 SELECT o.code,
    o.tx_id,
    o.idx,
    o.script,
    o.value,
    o.asset_id,
    o.input_tx_id,
    o.input_idx,
    o.input_mempool,
    o.immature,
    o.blk_id,
    o.blk_idx,
    o.blk_height,
    o.mempool,
    o.replaced_by,
    o.seen_at
   FROM outs o
  WHERE (((o.blk_id IS NOT NULL) OR ((o.mempool IS TRUE) AND (o.replaced_by IS NULL))) AND ((o.input_tx_id IS NULL) OR (o.input_mempool IS TRUE)));

CREATE TABLE wallets (
    wallet_id text NOT NULL,
    metadata jsonb,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE VIEW wallets_utxos AS
 SELECT q.wallet_id,
    u.code,
    u.tx_id,
    u.idx,
    u.script,
    u.value,
    u.asset_id,
    u.input_tx_id,
    u.input_idx,
    u.input_mempool,
    u.immature,
    u.blk_id,
    u.blk_idx,
    u.blk_height,
    u.mempool,
    u.replaced_by,
    u.seen_at
   FROM utxos u,
    LATERAL ( SELECT ws.wallet_id,
            ws.code,
            ws.script
           FROM wallets_scripts ws
          WHERE ((ws.code = u.code) AND (ws.script = u.script))) q;

CREATE VIEW wallets_balances AS
 SELECT wallets_utxos.wallet_id,
    wallets_utxos.code,
    wallets_utxos.asset_id,
    COALESCE(sum(wallets_utxos.value) FILTER (WHERE (wallets_utxos.input_mempool IS FALSE)), (0)::numeric) AS unconfirmed_balance,
    COALESCE(sum(wallets_utxos.value) FILTER (WHERE (wallets_utxos.blk_id IS NOT NULL)), (0)::numeric) AS confirmed_balance,
    COALESCE(sum(wallets_utxos.value) FILTER (WHERE ((wallets_utxos.input_mempool IS FALSE) AND (wallets_utxos.immature IS FALSE))), (0)::numeric) AS available_balance,
    COALESCE(sum(wallets_utxos.value) FILTER (WHERE (wallets_utxos.immature IS TRUE)), (0)::numeric) AS immature_balance
   FROM wallets_utxos
  GROUP BY wallets_utxos.wallet_id, wallets_utxos.code, wallets_utxos.asset_id;

CREATE MATERIALIZED VIEW wallets_history AS
 SELECT q.wallet_id,
    q.code,
    q.asset_id,
    q.tx_id,
    q.seen_at,
    q.blk_height,
    q.blk_idx,
    q.balance_change,
    sum(q.balance_change) OVER (PARTITION BY q.wallet_id, q.code, q.asset_id ORDER BY q.seen_at, q.blk_height, q.blk_idx) AS balance_total,
    rank() OVER (PARTITION BY q.wallet_id, q.code, q.asset_id ORDER BY q.seen_at, q.blk_height, q.blk_idx) AS nth
   FROM ( SELECT q_1.wallet_id,
            io.code,
            io.asset_id,
            min(io.blk_idx) AS blk_idx,
            min(io.blk_height) AS blk_height,
            io.tx_id,
            min(io.seen_at) AS seen_at,
            (COALESCE(sum(io.value) FILTER (WHERE (io.is_out IS TRUE)), (0)::numeric) - COALESCE(sum(io.value) FILTER (WHERE (io.is_out IS FALSE)), (0)::numeric)) AS balance_change
           FROM ins_outs io,
            LATERAL ( SELECT ts.wallet_id,
                    ts.code,
                    ts.script
                   FROM wallets_scripts ts
                  WHERE ((ts.code = io.code) AND (ts.script = io.script))) q_1
          WHERE (io.blk_id IS NOT NULL)
          GROUP BY q_1.wallet_id, io.code, io.asset_id, io.tx_id) q
  WITH NO DATA;

CREATE TABLE wallets_wallets (
    wallet_id text NOT NULL,
    parent_id text NOT NULL
);

ALTER TABLE ONLY blks
    ADD CONSTRAINT blks_pkey PRIMARY KEY (code, blk_id);

ALTER TABLE ONLY blks_txs
    ADD CONSTRAINT blks_txs_pkey PRIMARY KEY (code, tx_id, blk_id);

ALTER TABLE ONLY descriptors
    ADD CONSTRAINT descriptors_pkey PRIMARY KEY (code, descriptor);

ALTER TABLE ONLY descriptors_scripts
    ADD CONSTRAINT descriptors_scripts_pkey PRIMARY KEY (code, descriptor, idx) INCLUDE (script);

ALTER TABLE ONLY ins_outs
    ADD CONSTRAINT ins_outs_pkey PRIMARY KEY (code, tx_id, idx, is_out);

ALTER TABLE ONLY ins
    ADD CONSTRAINT ins_pkey PRIMARY KEY (code, tx_id, idx);

ALTER TABLE ONLY nbxv1_evts_ids
    ADD CONSTRAINT nbxv1_evts_ids_pkey PRIMARY KEY (code);

ALTER TABLE ONLY nbxv1_evts
    ADD CONSTRAINT nbxv1_evts_pkey PRIMARY KEY (code, id);

ALTER TABLE ONLY nbxv1_metadata
    ADD CONSTRAINT nbxv1_metadata_pkey PRIMARY KEY (wallet_id, key);

ALTER TABLE ONLY nbxv1_settings
    ADD CONSTRAINT nbxv1_settings_pkey PRIMARY KEY (code, key);

ALTER TABLE ONLY outs
    ADD CONSTRAINT outs_pkey PRIMARY KEY (code, tx_id, idx) INCLUDE (script, value, asset_id);

ALTER TABLE ONLY scripts
    ADD CONSTRAINT scripts_pkey PRIMARY KEY (code, script);

ALTER TABLE ONLY spent_outs
    ADD CONSTRAINT spent_outs_pkey PRIMARY KEY (code, tx_id, idx);

ALTER TABLE ONLY txs
    ADD CONSTRAINT txs_pkey PRIMARY KEY (code, tx_id);

ALTER TABLE ONLY wallets_descriptors
    ADD CONSTRAINT wallets_descriptors_pkey PRIMARY KEY (code, descriptor, wallet_id);

ALTER TABLE ONLY wallets
    ADD CONSTRAINT wallets_pkey PRIMARY KEY (wallet_id);

ALTER TABLE ONLY wallets_scripts
    ADD CONSTRAINT wallets_scripts_pkey PRIMARY KEY (code, script, wallet_id);

ALTER TABLE ONLY wallets_wallets
    ADD CONSTRAINT wallets_wallets_pkey PRIMARY KEY (wallet_id, parent_id);

CREATE INDEX blks_code_height_idx ON blks USING btree (code, height DESC) WHERE (confirmed IS TRUE);

CREATE INDEX descriptors_scripts_code_script ON descriptors_scripts USING btree (code, script);

CREATE INDEX descriptors_scripts_unused_idx ON descriptors_scripts USING btree (code, descriptor, idx) WHERE (used IS FALSE);

CREATE INDEX ins_code_spentoutpoint_txid_idx ON ins USING btree (code, spent_tx_id, spent_idx) INCLUDE (tx_id, idx);

CREATE INDEX ins_outs_by_code_scripts_idx ON ins_outs USING btree (code, script);

CREATE INDEX ins_outs_seen_at_idx ON ins_outs USING btree (seen_at, blk_height, blk_idx);

CREATE INDEX nbxv1_evts_code_id ON nbxv1_evts USING btree (code, id DESC);

CREATE INDEX nbxv1_evts_id ON nbxv1_evts USING btree (id DESC);

CREATE INDEX outs_unspent_idx ON outs USING btree (code) WHERE (((blk_id IS NOT NULL) OR ((mempool IS TRUE) AND (replaced_by IS NULL))) AND ((input_tx_id IS NULL) OR (input_mempool IS TRUE)));

CREATE INDEX scripts_by_wallet_id_idx ON wallets_scripts USING btree (wallet_id);

CREATE INDEX txs_by_blk_id ON blks_txs USING btree (code, blk_id);

CREATE INDEX txs_code_immature_idx ON txs USING btree (code) INCLUDE (tx_id) WHERE (immature IS TRUE);

CREATE INDEX txs_unconf_idx ON txs USING btree (code) INCLUDE (tx_id) WHERE (mempool IS TRUE);

CREATE INDEX wallets_descriptors_by_wallet_id_idx ON wallets_descriptors USING btree (wallet_id);

CREATE INDEX wallets_history_by_seen_at ON wallets_history USING btree (seen_at);

CREATE UNIQUE INDEX wallets_history_pk ON wallets_history USING btree (wallet_id, code, asset_id, tx_id);

CREATE INDEX wallets_wallets_parent_id ON wallets_wallets USING btree (parent_id);

CREATE TRIGGER blks_confirmed_trigger AFTER UPDATE ON blks FOR EACH ROW EXECUTE FUNCTION public.blks_confirmed_update_txs();

CREATE TRIGGER blks_txs_insert_trigger AFTER INSERT ON blks_txs FOR EACH ROW EXECUTE FUNCTION public.blks_txs_denormalize();

CREATE TRIGGER descriptors_scripts_after_insert_or_update_trigger BEFORE INSERT OR UPDATE ON descriptors_scripts FOR EACH ROW EXECUTE FUNCTION public.descriptors_scripts_after_insert_or_update_trigger_proc();

CREATE TRIGGER descriptors_scripts_wallets_scripts_trigger AFTER INSERT ON descriptors_scripts REFERENCING NEW TABLE AS new_descriptors_scripts FOR EACH STATEMENT EXECUTE FUNCTION public.descriptors_scripts_wallets_scripts_trigger_proc();

CREATE TRIGGER ins_after_insert2_trigger AFTER INSERT ON ins FOR EACH ROW EXECUTE FUNCTION public.ins_after_insert2_trigger_proc();

CREATE TRIGGER ins_after_insert_trigger AFTER INSERT ON ins REFERENCING NEW TABLE AS new_ins FOR EACH STATEMENT EXECUTE FUNCTION public.ins_after_insert_trigger_proc();

CREATE TRIGGER ins_after_update_trigger BEFORE UPDATE ON ins FOR EACH ROW EXECUTE FUNCTION public.ins_after_update_trigger_proc();

CREATE TRIGGER ins_before_insert_trigger BEFORE INSERT ON ins FOR EACH ROW EXECUTE FUNCTION public.ins_before_insert_trigger_proc();

CREATE TRIGGER ins_delete_trigger BEFORE DELETE ON ins FOR EACH ROW EXECUTE FUNCTION public.ins_delete_ins_outs();

CREATE TRIGGER outs_after_update_trigger AFTER UPDATE ON outs FOR EACH ROW EXECUTE FUNCTION public.outs_after_update_trigger_proc();

CREATE TRIGGER outs_before_insert_trigger BEFORE INSERT ON outs FOR EACH ROW EXECUTE FUNCTION public.outs_denormalize_from_tx();

CREATE TRIGGER outs_delete_trigger BEFORE DELETE ON outs FOR EACH ROW EXECUTE FUNCTION public.outs_delete_ins_outs();

CREATE TRIGGER outs_insert_trigger AFTER INSERT ON outs REFERENCING NEW TABLE AS new_outs FOR EACH STATEMENT EXECUTE FUNCTION public.outs_denormalize_to_ins_outs();

CREATE TRIGGER scripts_update_trigger AFTER UPDATE ON scripts FOR EACH ROW EXECUTE FUNCTION public.scripts_set_descriptors_scripts_used();

CREATE TRIGGER txs_insert_trigger AFTER UPDATE ON txs FOR EACH ROW EXECUTE FUNCTION public.txs_denormalize();

CREATE TRIGGER wallets_descriptors_after_delete_trigger AFTER DELETE ON wallets_descriptors REFERENCING OLD TABLE AS new_wallets_descriptors FOR EACH STATEMENT EXECUTE FUNCTION public.wallets_descriptors_after_delete_trigger_proc();

CREATE TRIGGER wallets_descriptors_after_insert_trigger AFTER INSERT ON wallets_descriptors REFERENCING NEW TABLE AS new_wallets_descriptors FOR EACH STATEMENT EXECUTE FUNCTION public.wallets_descriptors_after_insert_trigger_proc();

CREATE TRIGGER wallets_scripts_after_delete_trigger AFTER DELETE ON wallets_scripts REFERENCING OLD TABLE AS new_wallets_scripts FOR EACH STATEMENT EXECUTE FUNCTION public.wallets_scripts_after_delete_trigger_proc();

CREATE TRIGGER wallets_scripts_after_insert_trigger AFTER INSERT ON wallets_scripts REFERENCING NEW TABLE AS new_wallets_scripts FOR EACH STATEMENT EXECUTE FUNCTION public.wallets_scripts_after_insert_trigger_proc();

CREATE TRIGGER wallets_scripts_after_update_trigger AFTER UPDATE ON wallets_scripts REFERENCING NEW TABLE AS new_wallets_scripts FOR EACH STATEMENT EXECUTE FUNCTION public.wallets_scripts_after_update_trigger_proc();

CREATE TRIGGER wallets_wallets_after_delete_trigger AFTER DELETE ON wallets_wallets REFERENCING OLD TABLE AS new_wallets_wallets FOR EACH STATEMENT EXECUTE FUNCTION public.wallets_wallets_after_delete_trigger_proc();

CREATE TRIGGER wallets_wallets_after_insert_trigger AFTER INSERT ON wallets_wallets REFERENCING NEW TABLE AS new_wallets_wallets FOR EACH STATEMENT EXECUTE FUNCTION public.wallets_wallets_after_insert_trigger_proc();

CREATE TRIGGER wallets_wallets_before_insert_trigger BEFORE INSERT ON wallets_wallets FOR EACH ROW EXECUTE FUNCTION public.wallets_wallets_before_insert_trigger_proc();

ALTER TABLE ONLY blks_txs
    ADD CONSTRAINT blks_txs_code_blk_id_fkey FOREIGN KEY (code, blk_id) REFERENCES blks(code, blk_id) ON DELETE CASCADE;

ALTER TABLE ONLY blks_txs
    ADD CONSTRAINT blks_txs_code_tx_id_fkey FOREIGN KEY (code, tx_id) REFERENCES txs(code, tx_id) ON DELETE CASCADE;

ALTER TABLE ONLY descriptors_scripts
    ADD CONSTRAINT descriptors_scripts_code_script_fkey FOREIGN KEY (code, script) REFERENCES scripts(code, script) ON DELETE CASCADE;

ALTER TABLE ONLY ins
    ADD CONSTRAINT ins_code_script_fkey FOREIGN KEY (code, script) REFERENCES scripts(code, script) ON DELETE CASCADE;

ALTER TABLE ONLY ins
    ADD CONSTRAINT ins_code_spent_tx_id_fkey FOREIGN KEY (code, spent_tx_id) REFERENCES txs(code, tx_id) ON DELETE CASCADE;

ALTER TABLE ONLY ins
    ADD CONSTRAINT ins_code_spent_tx_id_spent_idx_fkey FOREIGN KEY (code, spent_tx_id, spent_idx) REFERENCES outs(code, tx_id, idx) ON DELETE CASCADE;

ALTER TABLE ONLY ins
    ADD CONSTRAINT ins_code_tx_id_fkey FOREIGN KEY (code, tx_id) REFERENCES txs(code, tx_id) ON DELETE CASCADE;

ALTER TABLE ONLY ins_outs
    ADD CONSTRAINT ins_outs_code_spent_tx_id_spent_idx_fkey FOREIGN KEY (code, spent_tx_id, spent_idx) REFERENCES outs(code, tx_id, idx);

ALTER TABLE ONLY nbxv1_metadata
    ADD CONSTRAINT nbxv1_metadata_wallet_id_fkey FOREIGN KEY (wallet_id) REFERENCES wallets(wallet_id) ON DELETE CASCADE;

ALTER TABLE ONLY outs
    ADD CONSTRAINT outs_code_script_fkey FOREIGN KEY (code, script) REFERENCES scripts(code, script) ON DELETE CASCADE;

ALTER TABLE ONLY outs
    ADD CONSTRAINT outs_code_tx_id_fkey FOREIGN KEY (code, tx_id) REFERENCES txs(code, tx_id) ON DELETE CASCADE;

ALTER TABLE ONLY outs
    ADD CONSTRAINT outs_spent_by_fk FOREIGN KEY (code, input_tx_id, input_idx) REFERENCES ins(code, tx_id, idx) ON DELETE SET NULL;

ALTER TABLE ONLY spent_outs
    ADD CONSTRAINT spent_outs_code_prev_spent_by_fkey FOREIGN KEY (code, prev_spent_by) REFERENCES txs(code, tx_id) ON DELETE CASCADE;

ALTER TABLE ONLY spent_outs
    ADD CONSTRAINT spent_outs_code_spent_by_fkey FOREIGN KEY (code, spent_by) REFERENCES txs(code, tx_id) ON DELETE CASCADE;

ALTER TABLE ONLY txs
    ADD CONSTRAINT txs_code_blk_id_fkey FOREIGN KEY (code, blk_id) REFERENCES blks(code, blk_id) ON DELETE SET NULL;

ALTER TABLE ONLY wallets_descriptors
    ADD CONSTRAINT wallets_descriptors_code_descriptor_fkey FOREIGN KEY (code, descriptor) REFERENCES descriptors(code, descriptor) ON DELETE CASCADE;

ALTER TABLE ONLY wallets_descriptors
    ADD CONSTRAINT wallets_descriptors_wallet_id_fkey FOREIGN KEY (wallet_id) REFERENCES wallets(wallet_id) ON DELETE CASCADE;

ALTER TABLE ONLY wallets_scripts
    ADD CONSTRAINT wallets_scripts_code_script_fkey FOREIGN KEY (code, script) REFERENCES scripts(code, script) ON DELETE CASCADE;

ALTER TABLE ONLY wallets_scripts
    ADD CONSTRAINT wallets_scripts_wallet_id_fkey FOREIGN KEY (wallet_id) REFERENCES wallets(wallet_id) ON DELETE CASCADE;

ALTER TABLE ONLY wallets_wallets
    ADD CONSTRAINT wallets_wallets_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES wallets(wallet_id) ON DELETE CASCADE;

ALTER TABLE ONLY wallets_wallets
    ADD CONSTRAINT wallets_wallets_wallet_id_fkey FOREIGN KEY (wallet_id) REFERENCES wallets(wallet_id) ON DELETE CASCADE;

CREATE TABLE nbxv1_migrations (
    script_name text NOT NULL,
    executed_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO nbxv1_migrations VALUES ('001.Migrations');
INSERT INTO nbxv1_migrations VALUES ('002.Model');
INSERT INTO nbxv1_migrations VALUES ('003.Legacy');
INSERT INTO nbxv1_migrations VALUES ('004.Fixup');
INSERT INTO nbxv1_migrations VALUES ('005.ToBTCFix');
INSERT INTO nbxv1_migrations VALUES ('006.GetWalletsRecent2');
INSERT INTO nbxv1_migrations VALUES ('007.FasterSaveMatches');
INSERT INTO nbxv1_migrations VALUES ('008.FasterGetUnused');
INSERT INTO nbxv1_migrations VALUES ('009.FasterGetUnused2');
INSERT INTO nbxv1_migrations VALUES ('010.ChangeEventsIdType');
INSERT INTO nbxv1_migrations VALUES ('011.FixGetWalletsRecent');
INSERT INTO nbxv1_migrations VALUES ('012.PerfFixGetWalletsRecent');
INSERT INTO nbxv1_migrations VALUES ('013.FixTrackedTransactions');
INSERT INTO nbxv1_migrations VALUES ('014.FixAddressReuse');
INSERT INTO nbxv1_migrations VALUES ('015.AvoidWAL');
INSERT INTO nbxv1_migrations VALUES ('016.FixTempTableCreation');
INSERT INTO nbxv1_migrations VALUES ('017.FixDoubleSpendDetection');
INSERT INTO nbxv1_migrations VALUES ('018.FastWalletRecent');
INSERT INTO nbxv1_migrations VALUES ('019.FixDoubleSpendDetection2');
INSERT INTO nbxv1_migrations VALUES ('020.ReplacingShouldBeIdempotent');
INSERT INTO nbxv1_migrations VALUES ('021.KeyPathInfoReturnsWalletId');
INSERT INTO nbxv1_migrations VALUES ('022.WalletsWalletsParentIdIndex');
INSERT INTO nbxv1_migrations VALUES ('023.KeyPathInfoReturnsIndex');
INSERT INTO nbxv1_migrations VALUES ('024.TrackedTxsReturnsFeature');
INSERT INTO nbxv1_migrations VALUES ('025.TrackedTxReturnsDescriptorMetadata');

ALTER TABLE ONLY nbxv1_migrations
    ADD CONSTRAINT nbxv1_migrations_pkey PRIMARY KEY (script_name);