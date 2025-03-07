
CREATE OR REPLACE FUNCTION get_nbxv1_keypath_info(
    input_code TEXT,
    input_script TEXT
)
RETURNS TABLE (
    code TEXT,
    script TEXT,
    addr TEXT,
    descriptor_metadata JSONB,
    keypath TEXT,
    descriptors_scripts_metadata JSONB,
    wallet_id TEXT,
    idx BIGINT,
    used BOOLEAN,
    descriptor TEXT
) AS $$
BEGIN
    RETURN QUERY
    WITH filtered_wallets AS MATERIALIZED (
      SELECT * FROM wallets_scripts WHERE wallets_scripts.code = input_code AND wallets_scripts.script = input_script
    )
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
    FROM ((filtered_wallets ws
       JOIN scripts s ON (((s.code = ws.code) AND (s.script = ws.script))))
       LEFT JOIN ((wallets_descriptors wd
       JOIN descriptors_scripts ds ON (((ds.code = wd.code) AND (ds.descriptor = wd.descriptor))))
       JOIN descriptors d ON (((d.code = ds.code) AND (d.descriptor = ds.descriptor)))) ON (((wd.wallet_id = ws.wallet_id) AND (wd.code = ws.code) AND (ds.script = ws.script))));
END;
$$ LANGUAGE plpgsql;