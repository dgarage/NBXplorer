CREATE OR REPLACE PROCEDURE save_matches(in_code TEXT, in_seen_at TIMESTAMPTZ) LANGUAGE plpgsql AS $$
DECLARE
  r RECORD;
BEGIN
  
  INSERT INTO txs (code, tx_id, seen_at)
  SELECT in_code, q.tx_id, in_seen_at FROM
  (
	SELECT tx_id FROM matched_outs
	UNION
	SELECT tx_id FROM matched_ins
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