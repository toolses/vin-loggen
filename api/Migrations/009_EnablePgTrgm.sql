-- Enable the pg_trgm extension for fuzzy text matching in dedup queries.
-- This is safe to call repeatedly (IF NOT EXISTS).
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- Add trigram indexes on the columns used for fuzzy dedup matching.
CREATE INDEX IF NOT EXISTS idx_wines_producer_trgm ON wines USING gin (LOWER(TRIM(producer)) gin_trgm_ops);
CREATE INDEX IF NOT EXISTS idx_wines_name_trgm     ON wines USING gin (LOWER(TRIM(name))     gin_trgm_ops);
