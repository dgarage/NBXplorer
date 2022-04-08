-- For documentation see https://github.com/dgarage/NBXplorer/tree/master/docs/Postgres-Schema.md
-- This file contains additional comments about column meaning.

-- The main tables are blks, blks_txs, txs, ins, outs, ins_outs, descriptors, desriptors_scripts, scripts, wallets, wallets_descriptors, wallets_scripts.
-- Those represent common concepts in the UTXO model.
-- Note that the model is heavily denormalized. Columns of txs are present in ins, outs, ins_outs.
-- ins_outs represent the same informations as ins and outs, but in a single table indexed with a timestamp.
-- The denormalization is kept up to date thanks to a bunch of triggers.
-- As such, an indexer just have to:
-- 1. Insert block in blks with confirmed='f'
-- 2. Call fetch_matches with all the ins and outs of a block
-- 3. This will create matched_outs, matched_ins and matched_conflicts the indexer can use to inspect what has been matched
-- 4. Call save_matches to instruct the database to insert all matched ins/outs.
-- 5. Turn confirmed='t' of the block.
-- The indexer is also responsible for creating wallets, associating descriptor, deriving scripts and adding them in descriptors_scripts.
-- There is one materialized view called wallets_history, which provide an history of wallets (time ordered list of wallet_id, code, asset_id, balance-change, total-balance)
-- refreshing this view is quite heavy (It can take between 5 and 10 seconds for huge database).
-- This view is specifically useful for reports and creating histograms via get_wallets_histogram.
-- If you want just the latest transactions from a wallet for UI purpose, do not use wallets_history, instead use the function get_wallets_recent.
-- get_wallets_recent doesn't depend on the materialized view, and so is always up to date.
-- Another useful views are wallets_balances, wallets_utxos.

CREATE TABLE IF NOT EXISTS blks (
  code TEXT NOT NULL,
  blk_id TEXT NOT NULL,
  height BIGINT,
  prev_id TEXT,
  confirmed BOOLEAN DEFAULT 'f',
  indexed_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (code, blk_id));
CREATE INDEX IF NOT EXISTS blks_code_height_idx ON blks (code, height DESC) WHERE confirmed IS TRUE;


-- This trigger update txs depending on the state of the block's confirmation.
-- Update immaturity, mempool, replaced_by, blk_height, blk_id and blk_idx of transactions 
CREATE OR REPLACE FUNCTION blks_confirmed_update_txs() RETURNS trigger LANGUAGE plpgsql AS $$
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
  WHERE so.code = NEW.code AND NOT so.tx_id=ANY(
	SELECT tx_id FROM txs
	WHERE code=NEW.code AND mempool IS TRUE);

  RETURN NEW;
END
$$;
CREATE TRIGGER blks_confirmed_trigger AFTER UPDATE ON blks FOR EACH ROW EXECUTE PROCEDURE blks_confirmed_update_txs();

-- Indexers are only expected to set code, tx_id and optionally raw, metadata and immature. 
CREATE TABLE IF NOT EXISTS txs (
  code TEXT NOT NULL,
  tx_id TEXT NOT NULL,
  -- The raw data of transactions isn't really useful aside for book keeping. Indexers can ignore this column and save some space.
  raw BYTEA DEFAULT NULL,
  -- Any data that may be useful to the indexer
  metadata JSONB DEFAULT NULL,
  -- An immature transaction is a coinbase transaction with less than 100 conf.
  immature BOOLEAN DEFAULT 'f',
  -- Those columns get updated when a block confirm this transactions. Since they rarely change, we save them directly in the txs table.
  blk_id TEXT DEFAULT NULL,
  blk_idx INT DEFAULT NULL, -- blk_idx is the index of the transaction in the block. It may be useful when ordering transactions.
  blk_height BIGINT DEFAULT NULL,
  -- 
  mempool BOOLEAN DEFAULT 't',
  replaced_by TEXT DEFAULT NULL,
  seen_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (code, tx_id),
  FOREIGN KEY (code, blk_id) REFERENCES blks ON DELETE SET NULL);

CREATE INDEX IF NOT EXISTS txs_unconf_idx ON txs (code) INCLUDE (tx_id) WHERE mempool IS TRUE;
CREATE INDEX IF NOT EXISTS txs_code_immature_idx ON txs (code) INCLUDE (tx_id) WHERE immature IS TRUE;

-- This trigger duplicate columns blk_id, blk_idx, blk_height, mempool, replace_by and seen_at
-- to ins, outs, and ins_outs.
-- Also make sure that if a transaction is replaced, then all the children are as well.
CREATE OR REPLACE FUNCTION txs_denormalize() RETURNS trigger LANGUAGE plpgsql AS $$
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
CREATE TRIGGER txs_insert_trigger AFTER UPDATE ON txs FOR EACH ROW EXECUTE PROCEDURE txs_denormalize();


-- Get the tip (Note we don't returns blks directly, since it prevent function inlining)
CREATE OR REPLACE FUNCTION get_tip(in_code TEXT)
RETURNS TABLE(code TEXT, blk_id TEXT, height BIGINT, prev_id TEXT) AS $$
  SELECT code, blk_id, height, prev_id FROM blks WHERE code=in_code AND confirmed IS TRUE ORDER BY height DESC LIMIT 1
$$  LANGUAGE SQL STABLE;

CREATE TABLE IF NOT EXISTS blks_txs (
  code TEXT NOT NULL,
  blk_id TEXT NOT NULL,
  tx_id TEXT NOT NULL,
  blk_idx INT DEFAULT NULL, -- blk_idx is the index of the transaction in the block. It may be useful when ordering transactions.
  PRIMARY KEY(code, tx_id, blk_id),
  FOREIGN KEY(code, tx_id) REFERENCES txs ON DELETE CASCADE,
  FOREIGN KEY(code, blk_id) REFERENCES blks ON DELETE CASCADE);

CREATE INDEX IF NOT EXISTS txs_by_blk_id ON blks_txs (code, blk_id);

-- This will set blk_id, blk_idx, blk_height, mempool and replaced_by of any transactions added to a confirmed block.
CREATE OR REPLACE FUNCTION blks_txs_denormalize() RETURNS trigger LANGUAGE plpgsql AS $$
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
CREATE TRIGGER blks_txs_insert_trigger AFTER INSERT ON blks_txs FOR EACH ROW EXECUTE PROCEDURE blks_txs_denormalize();

-- Indexers are only supposed to set code, script and addr.
CREATE TABLE IF NOT EXISTS scripts (
  code TEXT NOT NULL,
  script TEXT NOT NULL,
  -- We don't really use addr anywhere, but it's something nice to have for queries
  addr TEXT NOT NULL,
  -- Automatically updated by the outs
  used BOOLEAN NOT NULL DEFAULT 'f',
  PRIMARY KEY(code, script)
);

-- descriptors_scripts used is used for gap limit calculation, so we need to update the flag when we detect a script has been used.
CREATE OR REPLACE FUNCTION scripts_set_descriptors_scripts_used() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF NEW.used != OLD.used THEN
    UPDATE descriptors_scripts ds SET used='t' WHERE code=NEW.code AND script=NEW.script AND used='f';
  END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER scripts_update_trigger AFTER UPDATE ON scripts FOR EACH ROW EXECUTE PROCEDURE scripts_set_descriptors_scripts_used();

-- Indexers are only supposed to set code, tx_id, idx, script, value, asset_id.
CREATE TABLE IF NOT EXISTS outs (
  code TEXT NOT NULL,
  tx_id TEXT NOT NULL,
  idx BIGINT NOT NULL,
  script TEXT NOT NULL,
  value BIGINT NOT NULL,
  asset_id TEXT NOT NULL DEFAULT '',
  -- The next fields are populated triggers.
  -- Information about input spending this output...
  input_tx_id TEXT DEFAULT NULL,
  input_idx BIGINT DEFAULT NULL,
  input_mempool BOOLEAN NOT NULL DEFAULT 'f',
  -- Denormalized data from the txs table...
  immature BOOLEAN NOT NULL DEFAULT 'f',
  blk_id TEXT DEFAULT NULL,
  blk_idx INT DEFAULT NULL,
  blk_height BIGINT DEFAULT NULL,
  mempool BOOLEAN DEFAULT 't',
  replaced_by TEXT DEFAULT NULL,
  seen_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
  /* PRIMARY KEY (code, tx_id, idx) (enforced with index), */
  /* FOREIGN KEY (code, input_tx_id, input_idx) REFERENCES ins (code, input_tx_id, input_idx) ON DELETE SET NULL, Circular deps */
  FOREIGN KEY (code, tx_id) REFERENCES txs ON DELETE CASCADE,
  FOREIGN KEY (code, script) REFERENCES scripts ON DELETE CASCADE);

CREATE INDEX IF NOT EXISTS outs_unspent_idx ON outs (code) WHERE (blk_id IS NOT NULL OR (mempool IS TRUE AND replaced_by IS NULL)) AND (input_tx_id IS NULL OR input_mempool IS TRUE);

-- Changes to the outs table should ripple through the ins_outs table.
CREATE OR REPLACE FUNCTION outs_denormalize_to_ins_outs() RETURNS trigger LANGUAGE plpgsql AS $$
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
		WHERE code=r.code AND script=r.script;
	END LOOP;
	RETURN NULL;
END
$$;
CREATE TRIGGER outs_insert_trigger AFTER INSERT ON outs REFERENCING NEW TABLE AS new_outs FOR EACH STATEMENT EXECUTE PROCEDURE outs_denormalize_to_ins_outs();

-- When an out is inserted, this trigger fill the denormalized fields from the txs
CREATE OR REPLACE FUNCTION outs_denormalize_from_tx() RETURNS trigger LANGUAGE plpgsql AS $$
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
CREATE TRIGGER outs_before_insert_trigger BEFORE INSERT ON outs FOR EACH ROW EXECUTE PROCEDURE outs_denormalize_from_tx();

-- Deleting outs should also delete the associated ins_outs
CREATE OR REPLACE FUNCTION outs_delete_ins_outs() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  DELETE FROM ins_outs io WHERE io.code=OLD.code AND io.tx_id=OLD.tx_id AND io.idx=OLD.idx AND io.is_out IS TRUE;
  DELETE FROM ins_outs io WHERE io.code=OLD.code AND io.spent_tx_id=OLD.tx_id AND io.spent_idx=OLD.idx AND io.is_out IS FALSE;
  RETURN OLD;
END
$$;
CREATE TRIGGER outs_delete_trigger BEFORE DELETE ON outs FOR EACH ROW EXECUTE PROCEDURE outs_delete_ins_outs();


-- It is possible that a out asset_id or value change if it got unblinded... (Elements only)
-- so we need to propage changes to ins and ins_outs
CREATE OR REPLACE FUNCTION outs_after_update_trigger_proc() RETURNS trigger LANGUAGE plpgsql AS $$
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
CREATE TRIGGER outs_after_update_trigger AFTER UPDATE ON outs FOR EACH ROW EXECUTE PROCEDURE outs_after_update_trigger_proc();
  
ALTER TABLE outs DROP CONSTRAINT IF EXISTS outs_pkey CASCADE;
CREATE UNIQUE INDEX IF NOT EXISTS outs_pkey ON outs (code, tx_id, idx) INCLUDE (script, value, asset_id);
ALTER TABLE outs ADD CONSTRAINT outs_pkey PRIMARY KEY USING INDEX outs_pkey;

-- The indexer is only suppose to set code, input_tx_id, input_idx, spent_tx_id and spent_idx
CREATE TABLE IF NOT EXISTS ins (
  code TEXT NOT NULL,
  tx_id TEXT NOT NULL,
  idx BIGINT NOT NULL,
  spent_tx_id TEXT NOT NULL,
  spent_idx BIGINT NOT NULL,
  -- Denormalized data from the spent outs
  script TEXT NOT NULL,
  value BIGINT NOT NULL,
  asset_id TEXT NOT NULL,
  -- Denormalized data which rarely change: Must be same as tx
  blk_id TEXT DEFAULT NULL,
  blk_idx INT DEFAULT NULL,
  blk_height BIGINT DEFAULT NULL,
  mempool BOOLEAN DEFAULT 't',
  replaced_by TEXT DEFAULT NULL,
  seen_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (code, tx_id, idx),
  FOREIGN KEY (code, spent_tx_id, spent_idx) REFERENCES outs (code, tx_id, idx) ON DELETE CASCADE,
  FOREIGN KEY (code, tx_id) REFERENCES txs ON DELETE CASCADE,
  FOREIGN KEY (code, spent_tx_id) REFERENCES txs (code, tx_id) ON DELETE CASCADE,
  FOREIGN KEY (code, script) REFERENCES scripts ON DELETE CASCADE);
CREATE INDEX IF NOT EXISTS ins_code_spentoutpoint_txid_idx ON ins (code, spent_tx_id, spent_idx) INCLUDE (tx_id, idx);


ALTER TABLE outs ADD CONSTRAINT outs_spent_by_fk FOREIGN KEY (code, input_tx_id, input_idx) REFERENCES ins (code, tx_id, idx) ON DELETE SET NULL;

-- Changes to the ins table should ripple through the ins_outs table.
CREATE OR REPLACE FUNCTION ins_after_insert_trigger_proc() RETURNS trigger LANGUAGE plpgsql AS $$
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
CREATE TRIGGER ins_after_insert_trigger AFTER INSERT ON ins REFERENCING NEW TABLE AS new_ins FOR EACH STATEMENT EXECUTE PROCEDURE ins_after_insert_trigger_proc();

-- When an ins is inserted, it shoud sent the spent_tx_id and spent_id of the outs it spends.
CREATE OR REPLACE FUNCTION ins_after_insert2_trigger_proc() RETURNS trigger LANGUAGE plpgsql AS $$
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
CREATE TRIGGER ins_after_insert2_trigger AFTER INSERT ON ins FOR EACH ROW EXECUTE PROCEDURE ins_after_insert2_trigger_proc();

-- This trigger will maintain the input_mempool field of outs.
CREATE OR REPLACE FUNCTION ins_after_update_trigger_proc() RETURNS trigger LANGUAGE plpgsql AS $$
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
CREATE TRIGGER ins_after_update_trigger BEFORE UPDATE ON ins FOR EACH ROW EXECUTE PROCEDURE ins_after_update_trigger_proc();

-- When an ins is inserted, this trigger fill the denormalized fields from the txs
CREATE OR REPLACE FUNCTION ins_before_insert_trigger_proc() RETURNS trigger LANGUAGE plpgsql AS $$
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
CREATE TRIGGER ins_before_insert_trigger BEFORE INSERT ON ins FOR EACH ROW EXECUTE PROCEDURE ins_before_insert_trigger_proc();

-- If an ins is deleted, so should ins_outs entry
CREATE OR REPLACE FUNCTION ins_delete_ins_outs() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  DELETE FROM ins_outs io WHERE io.code=OLD.code AND io.tx_id=OLD.tx_id AND io.idx=OLD.idx AND io.is_out IS FALSE;
  RETURN OLD;
END
$$;
CREATE TRIGGER ins_delete_trigger BEFORE DELETE ON ins FOR EACH ROW EXECUTE PROCEDURE ins_delete_ins_outs();

-- Indexers are only supposed to set code, descriptor and optionally metadata.
CREATE TABLE IF NOT EXISTS descriptors (
  code TEXT NOT NULL,
  descriptor TEXT NOT NULL,
  -- Custom data for the indexer (eg. keypathtemplate)
  metadata JSONB NULL DEFAULT NULL,
  -- next_idx and gap are updated during insertion or update to descriptors_scripts
  next_idx BIGINT DEFAULT 0,
  gap BIGINT DEFAULT 0,
  PRIMARY KEY (code, descriptor)
);

-- Indexers are only suppoed to set code, descriptor, idx, script and optionally metadata and used.
CREATE TABLE IF NOT EXISTS descriptors_scripts (
  code TEXT NOT NULL,
  descriptor TEXT NOT NULL,
  idx BIGINT NOT NULL,
  script TEXT NOT NULL,
  -- Custom data for the indexer (eg. redeem scripts)
  metadata JSONB DEFAULT NULL,
  -- And indexer can turn on this field, even if the script isn't use (eg. for reserving an address for example), this will update the descriptor's gap.
  used BOOLEAN NOT NULL DEFAULT 'f',
  /* PRIMARY KEY (code, descriptor, idx) , Enforced via index */
  FOREIGN KEY (code, script) REFERENCES scripts ON DELETE CASCADE
);
ALTER TABLE descriptors_scripts DROP CONSTRAINT IF EXISTS descriptors_scripts_pkey CASCADE;
CREATE UNIQUE INDEX IF NOT EXISTS descriptors_scripts_pkey ON descriptors_scripts (code, descriptor, idx) INCLUDE (script);
ALTER TABLE descriptors_scripts ADD CONSTRAINT descriptors_scripts_pkey PRIMARY KEY USING INDEX descriptors_scripts_pkey;
CREATE INDEX IF NOT EXISTS descriptors_scripts_code_script ON descriptors_scripts (code, script);

-- This trigger will add the scripts generated by the descriptor into the wallets_scripts
CREATE OR REPLACE FUNCTION descriptors_scripts_wallets_scripts_trigger_proc() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
  r RECORD;
BEGIN
  INSERT INTO wallets_scripts AS ws (code, script, wallet_id)
  SELECT ds.code, ds.script, wd.wallet_id FROM new_descriptors_scripts ds
  JOIN wallets_descriptors wd USING (code, descriptor)
  ON CONFLICT (code, script, wallet_id) DO UPDATE SET ref_count = ws.ref_count + 1;
  RETURN NULL;
END $$;
CREATE TRIGGER descriptors_scripts_wallets_scripts_trigger AFTER INSERT ON descriptors_scripts REFERENCING NEW TABLE AS new_descriptors_scripts FOR EACH STATEMENT EXECUTE PROCEDURE descriptors_scripts_wallets_scripts_trigger_proc();

-- Update the gap of the descriptor when 'used' is modified, or when a new descriptor_scripts is inserted.
-- Update descriptor's next_idx when a descriptor_scripts is inserted.
-- Set the descriptor's used flag to the script value on insert
CREATE OR REPLACE FUNCTION descriptors_scripts_after_insert_or_update_trigger_proc() RETURNS trigger LANGUAGE plpgsql AS $$
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
CREATE TRIGGER descriptors_scripts_after_insert_or_update_trigger BEFORE INSERT OR UPDATE ON descriptors_scripts FOR EACH ROW EXECUTE PROCEDURE descriptors_scripts_after_insert_or_update_trigger_proc();

CREATE TABLE IF NOT EXISTS wallets (
  wallet_id TEXT NOT NULL PRIMARY KEY,
  metadata JSONB DEFAULT NULL,
  created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP);

CREATE TABLE IF NOT EXISTS wallets_wallets (
  wallet_id TEXT NOT NULL REFERENCES wallets ON DELETE CASCADE,
  parent_id TEXT NOT NULL REFERENCES wallets ON DELETE CASCADE,
  PRIMARY KEY (wallet_id, parent_id));

-- Let's verify that the parent isn't a descendant of the child 
CREATE OR REPLACE FUNCTION wallets_wallets_before_insert_trigger_proc() RETURNS TRIGGER AS $$
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
$$ LANGUAGE 'plpgsql';
CREATE TRIGGER wallets_wallets_before_insert_trigger BEFORE INSERT ON wallets_wallets FOR EACH ROW EXECUTE PROCEDURE wallets_wallets_before_insert_trigger_proc();

-- After a child wallet is removed, it should remove all its scripts from the parent.
CREATE OR REPLACE FUNCTION wallets_wallets_after_delete_trigger_proc() RETURNS TRIGGER AS $$
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
$$ LANGUAGE 'plpgsql';
CREATE TRIGGER wallets_wallets_after_delete_trigger AFTER DELETE ON wallets_wallets REFERENCING OLD TABLE AS new_wallets_wallets FOR EACH STATEMENT EXECUTE PROCEDURE wallets_wallets_after_delete_trigger_proc();

-- After a child wallet is added, it should add all its scripts to the parent.
CREATE FUNCTION wallets_wallets_after_insert_trigger_proc() RETURNS TRIGGER AS $$
BEGIN
  INSERT INTO wallets_scripts AS ws (code, script, wallet_id)
  SELECT ws.code, ws.script, ww.parent_id FROM new_wallets_wallets ww
  JOIN wallets_scripts ws ON ws.wallet_id=ww.wallet_id
  ON CONFLICT (code, script, wallet_id) DO UPDATE SET ref_count = ws.ref_count + 1;
  RETURN NULL;
END;
$$ LANGUAGE 'plpgsql';
CREATE TRIGGER wallets_wallets_after_insert_trigger AFTER INSERT ON wallets_wallets REFERENCING NEW TABLE AS new_wallets_wallets FOR EACH STATEMENT EXECUTE PROCEDURE wallets_wallets_after_insert_trigger_proc();

-- The list of scripts owned by wallets.
-- Indexers expected to only set code, script and wallet_id.
-- Indexers shouldn't add to this table after inserting to descriptors_wallets, as a trigger will do it automatically.
CREATE TABLE IF NOT EXISTS wallets_scripts (
  code TEXT NOT NULL,
  script TEXT NOT NULL,
  wallet_id TEXT REFERENCES wallets ON DELETE CASCADE,
  -- This is maintained by trigger. Each script in a wallet can be referenced in several place
  -- (different descriptors, or different child wallets) so we need to keep the ref_count and
  -- only delete the row if it reach 0
  ref_count INT DEFAULT 1,
  PRIMARY KEY (code, script, wallet_id),
  FOREIGN KEY (code, script) REFERENCES scripts ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS scripts_by_wallet_id_idx ON wallets_scripts(wallet_id);

-- After a script is deleted from a wallet, it should be deleted as well from the parents.
CREATE FUNCTION wallets_scripts_after_delete_trigger_proc() RETURNS TRIGGER AS $$
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
$$ LANGUAGE 'plpgsql';
CREATE TRIGGER wallets_scripts_after_delete_trigger AFTER DELETE ON wallets_scripts REFERENCING OLD TABLE AS new_wallets_scripts FOR EACH STATEMENT EXECUTE PROCEDURE wallets_scripts_after_delete_trigger_proc();

-- After a child wallet is added, it should add all its scripts to the parent.
CREATE OR REPLACE FUNCTION wallets_scripts_after_insert_trigger_proc() RETURNS TRIGGER AS $$
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
$$ LANGUAGE 'plpgsql';
CREATE TRIGGER wallets_scripts_after_insert_trigger AFTER INSERT ON wallets_scripts REFERENCING NEW TABLE AS new_wallets_scripts FOR EACH STATEMENT EXECUTE PROCEDURE wallets_scripts_after_insert_trigger_proc();

-- After the ref_count reach 0, we should delete the row
CREATE OR REPLACE FUNCTION wallets_scripts_after_update_trigger_proc() RETURNS TRIGGER AS $$
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
$$ LANGUAGE 'plpgsql';
CREATE TRIGGER wallets_scripts_after_update_trigger AFTER UPDATE ON wallets_scripts REFERENCING NEW TABLE AS new_wallets_scripts FOR EACH STATEMENT EXECUTE PROCEDURE wallets_scripts_after_update_trigger_proc();

-- Wallets include descriptors.
CREATE TABLE IF NOT EXISTS wallets_descriptors (
  code TEXT NOT NULL,
  descriptor TEXT NOT NULL,
  wallet_id TEXT NOT NULL REFERENCES wallets ON DELETE CASCADE,
  PRIMARY KEY (code, descriptor, wallet_id),
  FOREIGN KEY (code, descriptor) REFERENCES descriptors ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS wallets_descriptors_by_wallet_id_idx ON wallets_descriptors(wallet_id);

-- If a wallet remove a descriptor, then it should remove all scripts belonging to the descriptor.
CREATE FUNCTION wallets_descriptors_after_delete_trigger_proc() RETURNS TRIGGER AS $$
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
$$ LANGUAGE 'plpgsql';
CREATE TRIGGER wallets_descriptors_after_delete_trigger AFTER DELETE ON wallets_descriptors REFERENCING OLD TABLE AS new_wallets_descriptors FOR EACH STATEMENT EXECUTE PROCEDURE wallets_descriptors_after_delete_trigger_proc();

-- If a wallet add a descriptor, then it should add all its scripts
CREATE FUNCTION wallets_descriptors_after_insert_trigger_proc() RETURNS TRIGGER AS $$
BEGIN
  INSERT INTO wallets_scripts AS ws (code, script, wallet_id)
	  SELECT ds.code, ds.script, wd.wallet_id FROM new_wallets_descriptors wd
	  JOIN descriptors_scripts ds USING (code, descriptor)
  ON CONFLICT (code, script, wallet_id) DO UPDATE SET ref_count = ws.ref_count + 1;
  RETURN NULL;
END;
$$ LANGUAGE 'plpgsql';
CREATE TRIGGER wallets_descriptors_after_insert_trigger AFTER INSERT ON wallets_descriptors REFERENCING NEW TABLE AS new_wallets_descriptors FOR EACH STATEMENT EXECUTE PROCEDURE wallets_descriptors_after_insert_trigger_proc();


-- Returns a log of inputs and outputs
-- This table is denormalized to improve performance on queries involving seen_at
-- If you want a view of the current in_outs wihtout conflict use
-- SELECT * FROM ins_outs
-- WHERE blk_id IS NOT NULL OR (mempool IS TRUE AND replaced_by IS NULL)
-- ORDER BY seen_at
CREATE TABLE IF NOT EXISTS ins_outs (
  code TEXT NOT NULL,
  -- The tx_id of the input or output
  tx_id TEXT NOT NULL,
  -- The idx of the input or the output
  idx BIGINT NOT NULL,
  is_out BOOLEAN NOT NULL,
  -- Only available for inputs (is_out IS FALSE)
  spent_tx_id TEXT,
  spent_idx BIGINT,
  ----
  script TEXT NOT NULL,
  value BIGINT NOT NULL,
  asset_id TEXT NOT NULL,
  -- Denormalized data which rarely change: Must be same as tx
  immature BOOLEAN DEFAULT NULL,
  blk_id TEXT DEFAULT NULL,
  blk_idx INT DEFAULT NULL,
  blk_height BIGINT DEFAULT NULL,
  mempool BOOLEAN DEFAULT 't',
  replaced_by TEXT DEFAULT NULL,
  seen_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (code, tx_id, idx, is_out),
  FOREIGN KEY (code, spent_tx_id, spent_idx) REFERENCES outs (code, tx_id, idx) -- outs_delete_ins_outs trigger will take care of deleting, no CASCADE
);
CREATE INDEX IF NOT EXISTS ins_outs_seen_at_idx ON ins_outs (seen_at, blk_height, blk_idx);
CREATE INDEX ins_outs_by_code_scripts_idx ON ins_outs (code, script);
-- Returns current UTXOs
-- Warning: It also returns the UTXO that are confirmed but spent in the mempool, as well as immature utxos.
--          If you want the available UTXOs which can be spent use 'WHERE input_mempool IS FALSE AND immature IS FALSE'.
CREATE OR REPLACE VIEW utxos AS
SELECT o.* FROM outs o
WHERE (o.blk_id IS NOT NULL OR (o.mempool IS TRUE AND o.replaced_by IS NULL)) AND (o.input_tx_id IS NULL OR o.input_mempool IS TRUE);

-- Returns UTXOs with their associate wallet
-- Warning: It also returns the UTXO that are confirmed but spent in the mempool, as well as immature utxos.
--          If you want the available UTXOs which can be spent use 'WHERE input_mempool IS FALSE AND immature IS FALSE'.
CREATE OR REPLACE VIEW wallets_utxos AS
SELECT q.wallet_id, u.* FROM utxos u,
LATERAL (SELECT ws.wallet_id, ws.code, ws.script
		 FROM wallets_scripts ws
         WHERE ws.code = u.code AND ws.script = u.script) q;

-- Returns the balances of a wallet.
-- Warning: A wallet without any balance may not appear as a row in this view
CREATE OR REPLACE VIEW wallets_balances AS
SELECT
	wallet_id,
	code,
	asset_id,
	-- The balance if all unconfirmed transactions, non-conflicting, were finally confirmed
	COALESCE(SUM(value) FILTER (WHERE input_mempool IS FALSE), 0) unconfirmed_balance,
	-- The balance only taking into accounts confirmed transactions
	COALESCE(SUM(value) FILTER (WHERE blk_id IS NOT NULL), 0) confirmed_balance,
	-- Same as unconfirmed_balance, removing immature utxos (utxos from a miner aged less than 100 blocks)
	COALESCE(SUM(value) FILTER (WHERE input_mempool IS FALSE AND immature IS FALSE), 0) available_balance,
	-- The total value of immature utxos (utxos from a miner aged less than 100 blocks)
	COALESCE(SUM(value) FILTER (WHERE immature IS TRUE), 0) immature_balance
FROM wallets_utxos
GROUP BY wallet_id, code, asset_id;

-- Only used for double spending detection
CREATE TABLE IF NOT EXISTS spent_outs (
  code TEXT NOT NULL,
  tx_id TEXT NOT NULL,
  idx BIGINT NOT NULL,
  spent_by TEXT NOT NULL,
  prev_spent_by TEXT DEFAULT NULL,
  spent_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (code, tx_id, idx),
  FOREIGN KEY (code, spent_by) REFERENCES txs ON DELETE CASCADE,
  FOREIGN KEY (code, prev_spent_by) REFERENCES txs ON DELETE CASCADE
);

-- Provide an history of wallets (time ordered list of wallet_id, code, asset_id, balance-change, total-balance)
-- This view is intensive to compute (for 220K transactions, it takes around 5 seconds)
-- This is meant to be used for reports and histograms.
-- If you want the latest history of a wallet, use get_wallets_recent instead.
-- Refresh with `SELECT wallets_history_refresh();`
CREATE MATERIALIZED VIEW IF NOT EXISTS wallets_history AS
	SELECT q.wallet_id,
		   q.code,
		   q.asset_id,
		   q.tx_id,
		   q.seen_at,
		   q.blk_height,
		   q.blk_idx,
		   q.balance_change,
		SUM(q.balance_change) OVER (PARTITION BY wallet_id, code, asset_id ORDER BY seen_at, blk_height, blk_idx) balance_total,
		RANK() OVER (PARTITION BY wallet_id, code, asset_id ORDER BY seen_at, blk_height, blk_idx) nth
		FROM (
			SELECT q.wallet_id,
				   io.code,
				   io.asset_id,
				   MIN(io.blk_idx) blk_idx,
				   MIN(io.blk_height) blk_height,
				   tx_id,
				   MIN(io.seen_at) seen_at,
				   COALESCE(SUM (value) FILTER (WHERE is_out IS TRUE), 0) -  COALESCE(SUM (value) FILTER (WHERE is_out IS FALSE), 0) balance_change
			FROM ins_outs io,
			LATERAL (SELECT ts.wallet_id, ts.code, ts.script
					 FROM wallets_scripts ts
					 WHERE ts.code = io.code AND ts.script = io.script) q
			WHERE blk_id IS NOT NULL
			GROUP BY wallet_id, io.code, io.asset_id, tx_id) q
WITH DATA;

-- Refresh
CREATE OR REPLACE FUNCTION wallets_history_refresh () RETURNS BOOLEAN AS $$
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
$$ LANGUAGE plpgsql;

CREATE UNIQUE INDEX wallets_history_pk ON wallets_history (wallet_id, code, asset_id, tx_id);
CREATE INDEX wallets_history_by_seen_at ON wallets_history (seen_at);

-- Technically this LIMIT clause is useless. However, without it the query planner
-- is unable to correctly estimate the numbers of row in generate_series
-- which cause JIT compilation, slowing down the query considerably
-- See https://dba.stackexchange.com/questions/310235/why-is-my-nested-loop-taking-so-much-time/310242#310242
CREATE OR REPLACE FUNCTION generate_series_fixed(in_from TIMESTAMPTZ, in_to TIMESTAMPTZ, in_interval INTERVAL) RETURNS TABLE(s TIMESTAMPTZ) AS $$
  SELECT generate_series(in_from, in_to, in_interval)
  LIMIT  (EXTRACT(EPOCH FROM (in_to - in_from))/EXTRACT(EPOCH FROM in_interval)) + 1; -- I am unsure about the exact formula, but over estimating 1 row is fine...
$$ LANGUAGE SQL STABLE;

-- Histogram depends on wallets_history, as such, you should make sure the materialized view is refreshed time for up-to-date histogram.
CREATE OR REPLACE FUNCTION get_wallets_histogram(in_wallet_id TEXT, in_code TEXT, in_asset_id TEXT, in_from TIMESTAMPTZ, in_to TIMESTAMPTZ, in_interval INTERVAL)
RETURNS TABLE(date TIMESTAMPTZ, balance_change BIGINT, balance BIGINT) AS $$
  SELECT s AS time,
  		change::bigint,
  		(SUM (q.change) OVER (ORDER BY s) + COALESCE((SELECT balance_total FROM wallets_history WHERE seen_at < in_from AND wallet_id=in_wallet_id AND code=in_code AND asset_id=in_asset_id ORDER BY seen_at DESC, blk_height DESC, blk_idx DESC LIMIT 1), 0))::BIGINT  AS balance
  FROM generate_series_fixed(in_from, in_to - in_interval, in_interval) s
  LEFT JOIN LATERAL (
	  SELECT s, COALESCE(SUM(balance_change),0) change FROM wallets_history
	  WHERE  s <= seen_at AND seen_at < s + in_interval AND wallet_id=in_wallet_id AND code=in_code AND asset_id=in_asset_id
  ) q USING (s)
$$ LANGUAGE SQL STABLE;

-- Useful view to see what has going on recently in a wallet. Doesn't depends on wallets_history.
-- Better to use on Postgres 13+, as it greatly benefits from incremental sort feature.
CREATE OR REPLACE FUNCTION get_wallets_recent(in_wallet_id TEXT, in_interval INTERVAL, in_limit INT, in_offset INT)
RETURNS TABLE(code TEXT, asset_id TEXT, tx_id TEXT, seen_at TIMESTAMPTZ, balance_change BIGINT, balance_total BIGINT) AS $$
  -- We need to materialize, if too many utxos, postgres just call this one over and over...
  -- however Postgres 11 doesn't support AS MATERIALIZED :(
  WITH this_balances AS (
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
		WHERE ((CURRENT_TIMESTAMP - in_interval) <= seen_at) AND (blk_id IS NOT NULL OR (mempool IS TRUE AND replaced_by IS NULL)) AND wallet_id=in_wallet_id
		GROUP BY io.code, io.asset_id, tx_id, seen_at, blk_height, blk_idx
		ORDER BY seen_at DESC, blk_height DESC, blk_idx DESC, asset_id
	LIMIT in_limit
  )
  SELECT q.code, q.asset_id, q.tx_id, q.seen_at, q.balance_change::BIGINT, (COALESCE((q.latest_balance - LAG(balance_change_sum, 1) OVER (PARTITION BY code, asset_id ORDER BY seen_at DESC, blk_height DESC, blk_idx DESC)), q.latest_balance))::BIGINT balance_total FROM
	  (SELECT q.*,
			  COALESCE((SELECT unconfirmed_balance FROM this_balances WHERE code=q.code AND asset_id=q.asset_id), 0) latest_balance,
			  SUM(q.balance_change) OVER (PARTITION BY code, asset_id ORDER BY seen_at DESC, blk_height DESC, blk_idx DESC) balance_change_sum FROM 
		  latest_txs q
	  ) q
  ORDER BY seen_at DESC, blk_height DESC, blk_idx DESC, asset_id
  OFFSET in_offset
$$ LANGUAGE SQL STABLE;

CREATE TYPE new_out AS (
  tx_id TEXT,
  idx BIGINT,
  script TEXT,
  "value" BIGINT,
  asset_id TEXT
);
CREATE TYPE new_in AS (
  tx_id TEXT,
  idx BIGINT,
  spent_tx_id TEXT,
  spent_idx BIGINT
);

-- Not strictly used for now but useful for indexer
CREATE TYPE outpoint AS (
  tx_id TEXT,
  idx BIGINT
);

-- fetch_matches will take a list of outputs and inputs, then save those that we are traking in temporary table matched_outs/matched_ins
-- save_matches will insert the matched_outs/matched_ins into the database.
-- We provide convenience functions for save_matches which do both at same time.

CREATE OR REPLACE PROCEDURE save_matches(in_code TEXT, in_outs new_out[], in_ins new_in[]) LANGUAGE plpgsql AS $$
BEGIN
  CALL save_matches (in_code, in_outs, in_ins, CURRENT_TIMESTAMP);
END $$;

-- Need to call fetch_matches first
CREATE OR REPLACE PROCEDURE save_matches(in_code TEXT) LANGUAGE plpgsql AS $$
BEGIN
  CALL save_matches (in_code, CURRENT_TIMESTAMP);
END $$;

CREATE OR REPLACE PROCEDURE save_matches(in_code TEXT, in_outs new_out[], in_ins new_in[], in_seen_at TIMESTAMPTZ) LANGUAGE plpgsql AS $$
BEGIN
  CALL fetch_matches (in_code, in_outs, in_ins);
  CALL save_matches(in_code, in_seen_at);
END $$;

-- Need to call fetch_matches first
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

-- Will create three temporary tables: matched_outs, matched_ins with the matches and matched_conflicts to see the conflicts
CREATE OR REPLACE PROCEDURE fetch_matches(in_code TEXT, in_outs new_out[], in_ins new_in[]) LANGUAGE plpgsql AS $$
DECLARE
  has_match BOOLEAN;
BEGIN
  CALL fetch_matches(in_code, in_outs, in_ins, has_match);
END $$;

CREATE OR REPLACE PROCEDURE fetch_matches(in_code TEXT, in_outs new_out[], in_ins new_in[], inout has_match BOOLEAN) LANGUAGE plpgsql AS $$
BEGIN
	DROP TABLE IF EXISTS matched_outs;
	DROP TABLE IF EXISTS matched_ins;
	DROP TABLE IF EXISTS matched_conflicts;
	DROP TABLE IF EXISTS new_ins;
	has_match := 'f';
	CREATE TEMPORARY TABLE matched_outs AS 
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
	CREATE TEMPORARY TABLE new_ins AS
	SELECT in_code code, i.* FROM unnest(in_ins) WITH ORDINALITY AS i(tx_id, idx, spent_tx_id, spent_idx, "order");

	CREATE TEMPORARY TABLE matched_ins AS
	SELECT * FROM
	  (SELECT i.*, o.script, o.value, o.asset_id  FROM new_ins i
	  JOIN outs o ON o.code=i.code AND o.tx_id=i.spent_tx_id AND o.idx=i.spent_idx
	  UNION ALL
	  SELECT i.*, o.script, o.value, o.asset_id  FROM new_ins i
	  JOIN matched_outs o ON i.spent_tx_id = o.tx_id AND i.spent_idx = o.idx) i
	ORDER BY "order";

	DELETE FROM new_ins
	WHERE NOT tx_id=ANY(SELECT tx_id FROM matched_ins) AND NOT tx_id=ANY(SELECT tx_id FROM matched_outs);

	CREATE TEMPORARY TABLE matched_conflicts AS
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

-- Helper functions to format BTC with 8 decimal
CREATE OR REPLACE FUNCTION to_btc(v BIGINT) RETURNS NUMERIC language SQL IMMUTABLE AS $$
	   SELECT ROUND(v::NUMERIC / 100000000, 8)
$$;
CREATE OR REPLACE FUNCTION to_btc(v NUMERIC) RETURNS NUMERIC language SQL IMMUTABLE AS $$
	   SELECT ROUND(v / 100000000, 8)
$$;