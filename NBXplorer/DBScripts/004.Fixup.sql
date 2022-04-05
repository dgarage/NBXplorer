-- Technically this LIMIT clause is useless. However, without it the query planner
-- is unable to correctly estimate the numbers of row in generate_series
-- which cause JIT compilation, slowing down the query considerably
-- See https://dba.stackexchange.com/questions/310235/why-is-my-nested-loop-taking-so-much-time/310242#310242

DROP FUNCTION generate_series_fixed;
CREATE OR REPLACE FUNCTION generate_series_fixed(in_from TIMESTAMPTZ, in_to TIMESTAMPTZ, in_interval INTERVAL) RETURNS TABLE(s TIMESTAMPTZ) AS $$
  SELECT generate_series(in_from, in_to, in_interval)
  LIMIT  (EXTRACT(EPOCH FROM (in_to - in_from))/EXTRACT(EPOCH FROM in_interval)) + 1; -- I am unsure about the exact formula, but over estimating 1 row is fine...
$$ LANGUAGE SQL STABLE;