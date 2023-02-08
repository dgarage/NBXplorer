CREATE OR REPLACE FUNCTION scripts_set_descriptors_scripts_used() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  IF NEW.used != OLD.used AND NEW.used IS TRUE THEN
    UPDATE descriptors_scripts ds SET used='t' WHERE code=NEW.code AND script=NEW.script AND used='f';
  END IF;
  RETURN NEW;
END $$;


CREATE OR REPLACE FUNCTION outs_denormalize_to_ins_outs() RETURNS trigger
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


-- Select all scripts that are used but without outs
-- Fix them to used='f'
-- This could happen because of bug during migration from DBTrie
UPDATE scripts s
SET used='f'
FROM (SELECT s.code, s.script FROM scripts s
LEFT JOIN outs o USING (code, script)
WHERE used='t' AND o.script is NULL) s2
WHERE s2.code=s.code AND s2.script=s.script AND s.used='t';


-- Make sure that all the descriptors_scripts which aren't used, but with scripts used are fixed to used
UPDATE descriptors_scripts ds
SET used='t'
FROM scripts s
WHERE s.code=ds.code AND s.script=ds.script AND ds.used='f' AND s.used = 't';