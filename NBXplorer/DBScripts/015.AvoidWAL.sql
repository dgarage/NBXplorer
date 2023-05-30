CREATE OR REPLACE PROCEDURE fetch_matches(in_code text, in_outs new_out[], in_ins new_in[], INOUT has_match boolean)
    LANGUAGE plpgsql
    AS $$
BEGIN
	BEGIN
	  TRUNCATE TABLE matched_outs, matched_ins, matched_conflicts, new_ins;
	EXCEPTION WHEN others THEN
	  CREATE TEMPORARY TABLE matched_outs (LIKE new_out);
	  ALTER TABLE matched_outs ADD COLUMN "order" BIGINT;

	  CREATE TEMPORARY TABLE new_ins (LIKE new_in);
	  ALTER TABLE new_ins ADD COLUMN "order" BIGINT;
	  ALTER TABLE new_ins ADD COLUMN code TEXT;

	  CREATE TEMPORARY TABLE matched_ins (LIKE new_ins);
	  ALTER TABLE matched_ins ADD COLUMN script TEXT;
	  ALTER TABLE matched_ins ADD COLUMN value bigint;
	  ALTER TABLE matched_ins ADD COLUMN asset_id TEXT;

	  CREATE TEMPORARY TABLE matched_conflicts (
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
	WHERE NOT tx_id=ANY(SELECT tx_id FROM matched_ins) AND NOT tx_id=ANY(SELECT tx_id FROM matched_outs);
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