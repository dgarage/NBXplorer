CREATE OR REPLACE PROCEDURE fetch_matches(in_code text, in_outs new_out[], in_ins new_in[], INOUT has_match boolean)
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
		replaced_tx_id TEXT);
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
	  SELECT in_code code, i.spent_tx_id, i.spent_idx, i.tx_id replacing_tx_id, so.spent_by replaced_tx_id FROM new_ins i
	  JOIN spent_outs so ON so.code=in_code AND so.tx_id=i.spent_tx_id AND so.idx=i.spent_idx
	  JOIN txs rt ON so.code=rt.code AND rt.tx_id=so.spent_by
	  WHERE so.spent_by != i.tx_id AND rt.code=in_code AND rt.mempool IS TRUE
	  UNION
	  SELECT c.code, c.spent_tx_id, c.spent_idx, c.replacing_tx_id, i.tx_id replaced_tx_id FROM cte c
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
	PERFORM 1 FROM matched_conflicts LIMIT 1;
	IF FOUND THEN
	  has_match := 't';
	END IF;
END $$;

CREATE OR REPLACE PROCEDURE save_matches(in_code text, in_seen_at timestamp with time zone)
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
	SELECT * FROM matched_conflicts
  LOOP
	UPDATE spent_outs SET spent_by=r.replacing_tx_id, prev_spent_by=r.replaced_tx_id
	WHERE code=r.code AND tx_id=r.spent_tx_id AND idx=r.spent_idx;
	UPDATE txs SET replaced_by=r.replacing_tx_id
	WHERE code=r.code AND tx_id=r.replaced_tx_id;
  END LOOP;
END $$;