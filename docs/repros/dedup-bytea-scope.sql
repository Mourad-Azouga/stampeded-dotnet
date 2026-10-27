-- Run from psql against a disposable PostgreSQL 18 database.
-- Everything, including the schema installation, is rolled back.
\set ON_ERROR_STOP on
SELECT 'repro_' || replace(gen_random_uuid()::text, '-', '') AS repro_schema \gset
BEGIN;
CREATE SCHEMA :"repro_schema";
SET LOCAL search_path TO :"repro_schema";
\ir ../../artifacts/native-source/schema.sql
SELECT stampeded_create_queue('probe');

INSERT INTO stampeded_jobs (queue, task, dedup_scope) VALUES ('probe', 'report', 'a');
INSERT INTO stampeded_jobs (queue, task, dedup_scope) VALUES ('probe', 'report', $$\x61$$)
ON CONFLICT DO NOTHING;

SELECT count(*) AS actual_jobs, 2 AS expected_distinct_jobs FROM stampeded_jobs;
-- Upstream 0.4.0 produces 1: text::bytea decodes both distinct scope strings as byte 0x61.
ROLLBACK;
