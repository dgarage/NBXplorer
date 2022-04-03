-- Those tables are specifically needed by NBXplorer indexer

CREATE TABLE IF NOT EXISTS nbxv1_evts (
  code TEXT NOT NULL,
  id SERIAL NOT NULL,
  type TEXT NOT NULL,
  data JSONB NOT NULL,
  created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (code, id)
);
CREATE TABLE IF NOT EXISTS nbxv1_evts_ids (
  code TEXT NOT NULL PRIMARY KEY,
  curr_id BIGINT
);

CREATE TABLE IF NOT EXISTS nbxv1_settings (
  code TEXT NOT NULL,
  key TEXT NOT NULL,
  data_bytes bytea DEFAULT NULL,
  data_json JSONB DEFAULT NULL,
  PRIMARY KEY (code, key)
);

CREATE TABLE IF NOT EXISTS nbxv1_metadata (
  wallet_id TEXT NOT NULL REFERENCES wallets ON DELETE CASCADE,
  key TEXT NOT NULL,
  data JSONB,
  PRIMARY KEY (wallet_id, key)
);

CREATE INDEX IF NOT EXISTS nbxv1_evts_id ON nbxv1_evts (id DESC);
CREATE INDEX IF NOT EXISTS nbxv1_evts_code_id ON nbxv1_evts (code, id DESC);


CREATE OR REPLACE FUNCTION nbxv1_get_keypath(metadata JSONB, idx BIGINT) RETURNS TEXT language SQL IMMUTABLE AS $$
	   SELECT CASE WHEN metadata->>'type' = 'NBXv1-Derivation' 
	   THEN REPLACE(metadata->>'keyPathTemplate', '*', idx::TEXT) 
	   ELSE NULL END
$$;

-- Algorithm chosen to have a nice random string in base64, without padding (easier to copy paste, and smaller than hex)
CREATE OR REPLACE FUNCTION nbxv1_get_wallet_id(in_code TEXT, in_scheme_or_address TEXT) RETURNS TEXT language SQL IMMUTABLE AS $$
	   SELECT encode(substring(sha256((in_code || '|' || in_scheme_or_address)::bytea), 0, 22), 'base64')
$$;

CREATE OR REPLACE FUNCTION nbxv1_get_descriptor_id(in_code TEXT, in_scheme TEXT, in_feature TEXT) RETURNS TEXT language SQL IMMUTABLE AS $$
	   SELECT encode(substring(sha256((in_code || '|' || in_scheme || '|' || in_feature)::bytea), 0, 22), 'base64')
$$;

CREATE OR REPLACE VIEW nbxv1_keypath_info AS
SELECT ws.code, ws.script, s.addr, d.metadata descriptor_metadata, nbxv1_get_keypath(d.metadata, ds.idx) keypath, ds.metadata descriptors_scripts_metadata
FROM wallets_scripts ws
JOIN scripts s ON s.code=ws.code AND s.script=ws.script
LEFT JOIN (wallets_descriptors wd
     JOIN descriptors_scripts ds ON ds.code=wd.code AND ds.descriptor=wd.descriptor
     JOIN descriptors d ON d.code=ds.code AND d.descriptor=ds.descriptor)
	 ON wd.wallet_id=ws.wallet_id AND wd.code=ws.code AND ds.script=ws.script;


CREATE OR REPLACE VIEW nbxv1_tracked_txs AS
SELECT ws.wallet_id, io.*, nbxv1_get_keypath(d.metadata, ds.idx) keypath
FROM wallets_scripts ws
JOIN ins_outs io ON io.code=ws.code AND io.script=ws.script
LEFT JOIN (wallets_descriptors wd
     JOIN descriptors_scripts ds ON ds.code=wd.code AND ds.descriptor=wd.descriptor
     JOIN descriptors d ON d.code=ds.code AND d.descriptor=ds.descriptor)
	 ON wd.wallet_id=ws.wallet_id AND wd.code=ws.code AND ds.script=ws.script;

CREATE TYPE nbxv1_ds AS (
  descriptor TEXT,
  idx BIGINT,
  script TEXT,
  metadata JSONB,
  addr TEXT,
  used BOOLEAN
);
