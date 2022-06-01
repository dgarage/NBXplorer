-- nbxv1_evts.id shouldn't be SERIAL

ALTER TABLE nbxv1_evts ALTER COLUMN id TYPE BIGINT;
DROP SEQUENCE nbxv1_evts_id_seq CASCADE;