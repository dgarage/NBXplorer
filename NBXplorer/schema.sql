CREATE DATABASE "nbxplorer" LC_COLLATE = 'C' TEMPLATE=template0 LC_CTYPE = 'C' ENCODING = 'UTF8';
-- Then connect to nbxplorer through "\c nbxplorer" or "use nbxplorer"
SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SET check_function_bodies = false;
SET client_min_messages = warning;
SET row_security = off;
CREATE EXTENSION IF NOT EXISTS plpgsql WITH SCHEMA pg_catalog;
COMMENT ON EXTENSION plpgsql IS 'PL/pgSQL procedural language';
CREATE FUNCTION insert_event(data_arg bytea, event_id_arg character varying) RETURNS bigint
    LANGUAGE plpgsql
    AS $$
DECLARE
        "inserted_id" BIGINT;
BEGIN
        PERFORM pg_advisory_xact_lock(183620);
        INSERT INTO "Events" ("data", "event_id") VALUES ("data_arg", "event_id_arg")
                RETURNING "id" INTO "inserted_id";
        RETURN "inserted_id";
EXCEPTION  WHEN unique_violation THEN
        SELECT "id" FROM "Events" WHERE "event_id" = "event_id_arg" INTO "inserted_id";
        RETURN "inserted_id";
END;
$$;

CREATE TABLE "Events" (
    id bigint NOT NULL,
    data bytea NOT NULL,
    event_id character varying(40)
);
CREATE SEQUENCE "Events_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;
ALTER SEQUENCE "Events_id_seq" OWNED BY "Events".id;
CREATE TABLE "GenericTables" (
    "PartitionKeyRowKey" text NOT NULL,
    "Value" bytea,
    "DeletedAt" timestamp without time zone
);
ALTER TABLE ONLY "Events" ALTER COLUMN id SET DEFAULT nextval('"Events_id_seq"'::regclass);
ALTER TABLE ONLY "Events"
    ADD CONSTRAINT "Events_event_id_key" UNIQUE (event_id);
ALTER TABLE ONLY "Events"
    ADD CONSTRAINT "Events_pkey" PRIMARY KEY (id);
ALTER TABLE ONLY "GenericTables"
    ADD CONSTRAINT "GenericTables_pkey" PRIMARY KEY ("PartitionKeyRowKey");
