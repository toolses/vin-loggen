-- 006_SplitWinesIntoLogsTable.sql
-- Splits the monolithic 'wines' table into:
--   wines      – master/catalogue data (producer, name, vintage, …)
--                Deduplicated; shared across all users.
--   wine_logs  – per-user tasting data (rating, notes, location, …)
--                One row per tasting event.
--
-- Migration steps
--   1. Add new master-data columns to wines
--   2. Create wine_logs with all user-specific columns
--   3. Back-fill wine_logs from existing wines rows
--   4. Deduplicate wines (keep oldest canonical per identity group)
--   5. Add UNIQUE constraint on wines (producer, name, vintage)
--   6. Drop user-specific columns from wines; update RLS
--   7. Enable RLS on wine_logs
--   8. Create wine_entries view (latest log per wine per user)

-- ── 1. New master-data columns ────────────────────────────────────────────────
ALTER TABLE wines ADD COLUMN IF NOT EXISTS grapes             TEXT[];
ALTER TABLE wines ADD COLUMN IF NOT EXISTS alcohol_content    NUMERIC(4,1);
ALTER TABLE wines ADD COLUMN IF NOT EXISTS external_source_id TEXT;

-- ── 2. Create wine_logs ───────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS wine_logs (
    id            UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    wine_id       UUID         NOT NULL REFERENCES wines(id) ON DELETE CASCADE,
    user_id       UUID         REFERENCES auth.users(id) ON DELETE CASCADE,
    rating        NUMERIC(2,1),
    notes         TEXT,
    image_url     TEXT,
    tasted_at     DATE,
    location_name TEXT,
    location_lat  DOUBLE PRECISION,
    location_lng  DOUBLE PRECISION,
    location_type TEXT CHECK (location_type IN ('restaurant', 'butikk', 'hjemme', 'annet')),
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_wine_logs_wine_id     ON wine_logs (wine_id);
CREATE INDEX IF NOT EXISTS idx_wine_logs_user_id     ON wine_logs (user_id);
CREATE INDEX IF NOT EXISTS idx_wine_logs_user_wine   ON wine_logs (user_id, wine_id, created_at DESC);

-- ── 3. Back-fill wine_logs from existing wines rows ───────────────────────────
-- Every existing row becomes one wine_log entry (preserving user, dates, etc.)
INSERT INTO wine_logs (
    wine_id, user_id, rating, notes, image_url, tasted_at,
    location_name, location_lat, location_lng, location_type, created_at
)
SELECT
    id, user_id, rating, notes, image_url, tasted_at,
    location_name, location_lat, location_lng, location_type, created_at
FROM wines
WHERE user_id IS NOT NULL;

-- ── 4. Deduplicate wines ──────────────────────────────────────────────────────
-- For each identity group (producer, name, vintage – case-insensitive), keep
-- the earliest-created row as the canonical record and point all wine_logs at it.

-- 4a. Reassign wine_logs from duplicate rows to their canonical row
WITH canonical AS (
    SELECT DISTINCT ON (
               LOWER(TRIM(producer)),
               LOWER(TRIM(name)),
               COALESCE(vintage::TEXT, '')
           )
           id                            AS canonical_id,
           LOWER(TRIM(producer))         AS lp,
           LOWER(TRIM(name))             AS ln,
           COALESCE(vintage::TEXT, '')   AS lv
    FROM   wines
    ORDER  BY
           LOWER(TRIM(producer)),
           LOWER(TRIM(name)),
           COALESCE(vintage::TEXT, ''),
           created_at
),
non_canonical AS (
    SELECT w.id AS dup_id, c.canonical_id
    FROM   wines w
    JOIN   canonical c
           ON  LOWER(TRIM(w.producer))       = c.lp
           AND LOWER(TRIM(w.name))           = c.ln
           AND COALESCE(w.vintage::TEXT, '') = c.lv
    WHERE  w.id <> c.canonical_id
)
UPDATE wine_logs
SET    wine_id = nc.canonical_id
FROM   non_canonical nc
WHERE  wine_logs.wine_id = nc.dup_id;

-- 4b. Delete the now-orphaned duplicate wine rows
DELETE FROM wines
WHERE id NOT IN (
    SELECT DISTINCT ON (
               LOWER(TRIM(producer)),
               LOWER(TRIM(name)),
               COALESCE(vintage::TEXT, '')
           )
           id
    FROM   wines
    ORDER  BY
           LOWER(TRIM(producer)),
           LOWER(TRIM(name)),
           COALESCE(vintage::TEXT, ''),
           created_at
);

-- ── 5. UNIQUE constraint on wines (producer, name, vintage) ──────────────────
-- NULLS NOT DISTINCT (PG 15+) treats NULL vintage values as equal, so
-- two wines with the same producer+name and NULL vintage conflict.
ALTER TABLE wines
    ADD CONSTRAINT wines_producer_name_vintage_unique
    UNIQUE NULLS NOT DISTINCT (producer, name, vintage);

-- ── 6. Drop user-specific columns from wines; update RLS ─────────────────────
-- Drop per-user policies first — they reference user_id and block the column drop.
DROP POLICY IF EXISTS "Users read own wines"    ON wines;
DROP POLICY IF EXISTS "Users insert own wines"  ON wines;
DROP POLICY IF EXISTS "Users update own wines"  ON wines;
DROP POLICY IF EXISTS "Users delete own wines"  ON wines;
DROP POLICY IF EXISTS "Read orphaned wines"     ON wines;

ALTER TABLE wines DROP COLUMN IF EXISTS user_id;
ALTER TABLE wines DROP COLUMN IF EXISTS rating;
ALTER TABLE wines DROP COLUMN IF EXISTS notes;
ALTER TABLE wines DROP COLUMN IF EXISTS image_url;
ALTER TABLE wines DROP COLUMN IF EXISTS tasted_at;
ALTER TABLE wines DROP COLUMN IF EXISTS location_name;
ALTER TABLE wines DROP COLUMN IF EXISTS location_lat;
ALTER TABLE wines DROP COLUMN IF EXISTS location_lng;
ALTER TABLE wines DROP COLUMN IF EXISTS location_type;

-- Replace per-user RLS with catalogue-level policies:
-- any authenticated user can read, insert, or update master wine data.
CREATE POLICY "Authenticated users can read wines"
    ON wines FOR SELECT
    TO authenticated
    USING (true);

CREATE POLICY "Authenticated users can insert wines"
    ON wines FOR INSERT
    TO authenticated
    WITH CHECK (true);

CREATE POLICY "Authenticated users can update wines"
    ON wines FOR UPDATE
    TO authenticated
    USING (true);

-- ── 7. RLS on wine_logs ───────────────────────────────────────────────────────
ALTER TABLE wine_logs ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Users read own wine_logs"
    ON wine_logs FOR SELECT
    USING (auth.uid() = user_id);

CREATE POLICY "Users insert own wine_logs"
    ON wine_logs FOR INSERT
    WITH CHECK (auth.uid() = user_id);

CREATE POLICY "Users update own wine_logs"
    ON wine_logs FOR UPDATE
    USING (auth.uid() = user_id);

CREATE POLICY "Users delete own wine_logs"
    ON wine_logs FOR DELETE
    USING (auth.uid() = user_id);

-- ── 8. wine_entries view ──────────────────────────────────────────────────────
-- Shows one row per (wine, user): the wine's master data joined with that
-- user's most-recent log, plus a count of all their logs for that wine.
--
-- security_invoker = true → the view inherits the calling user's RLS context,
-- so wine_logs RLS automatically filters to auth.uid().
CREATE OR REPLACE VIEW wine_entries
WITH (security_invoker = true) AS
WITH ranked AS (
    SELECT
        *,
        ROW_NUMBER() OVER (PARTITION BY wine_id ORDER BY created_at DESC) AS rn,
        COUNT(*)     OVER (PARTITION BY wine_id)                          AS log_count
    FROM wine_logs
    -- No WHERE needed; wine_logs RLS limits rows to the calling user
)
SELECT
    w.id,
    w.name,
    w.producer,
    w.vintage,
    w.type,
    w.country,
    w.region,
    w.grapes,
    w.alcohol_content,
    w.external_source_id,
    r.id            AS log_id,
    r.user_id,
    r.rating,
    r.notes,
    r.image_url,
    r.tasted_at,
    r.location_name,
    r.location_lat,
    r.location_lng,
    r.location_type,
    r.created_at,
    r.log_count
FROM   ranked r
JOIN   wines  w ON w.id = r.wine_id
WHERE  r.rn = 1;
