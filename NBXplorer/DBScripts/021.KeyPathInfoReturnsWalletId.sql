CREATE OR REPLACE VIEW nbxv1_keypath_info AS
 SELECT ws.code,
    ws.script,
    s.addr,
    d.metadata AS descriptor_metadata,
    nbxv1_get_keypath(d.metadata, ds.idx) AS keypath,
    ds.metadata AS descriptors_scripts_metadata,
    ws.wallet_id
   FROM ((wallets_scripts ws
     JOIN scripts s ON (((s.code = ws.code) AND (s.script = ws.script))))
     LEFT JOIN ((wallets_descriptors wd
     JOIN descriptors_scripts ds ON (((ds.code = wd.code) AND (ds.descriptor = wd.descriptor))))
     JOIN descriptors d ON (((d.code = ds.code) AND (d.descriptor = ds.descriptor)))) ON (((wd.wallet_id = ws.wallet_id) AND (wd.code = ws.code) AND (ds.script = ws.script))));