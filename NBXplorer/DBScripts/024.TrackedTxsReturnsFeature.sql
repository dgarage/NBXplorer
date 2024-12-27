CREATE OR REPLACE VIEW nbxv1_tracked_txs AS
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
    d.metadata->>'feature' feature
   FROM ((wallets_scripts ws
     JOIN ins_outs io ON (((io.code = ws.code) AND (io.script = ws.script))))
     LEFT JOIN ((wallets_descriptors wd
     JOIN descriptors_scripts ds ON (((ds.code = wd.code) AND (ds.descriptor = wd.descriptor))))
     JOIN descriptors d ON (((d.code = ds.code) AND (d.descriptor = ds.descriptor)))) ON (((wd.wallet_id = ws.wallet_id) AND (wd.code = ws.code) AND (ds.script = ws.script))))
  WHERE ((io.blk_id IS NOT NULL) OR (io.mempool IS TRUE));

-- Convert a template '0/1/*/2/3' and a keypath `0/1/123/2/3` to 123
CREATE OR REPLACE FUNCTION nbxv1_get_keypath_index(metadata JSONB, keypath TEXT) RETURNS BIGINT language SQL IMMUTABLE AS $$
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
$$;

CREATE OR REPLACE FUNCTION nbxv1_get_keypath(metadata jsonb, idx bigint) RETURNS text
    LANGUAGE sql IMMUTABLE
    AS $$
	   SELECT REPLACE(metadata->>'keyPathTemplate', '*', idx::TEXT)
$$;