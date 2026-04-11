-- ── Migration 008: Add Pro enrichment columns to wines ──────────────────────
-- Stores food pairings, description, and technical notes from AI/wineapi.io
-- enrichment at the master wine level (not per-tasting).

ALTER TABLE wines ADD COLUMN IF NOT EXISTS food_pairings   TEXT[];
ALTER TABLE wines ADD COLUMN IF NOT EXISTS description     TEXT;
ALTER TABLE wines ADD COLUMN IF NOT EXISTS technical_notes TEXT;

-- ── Recreate wine_entries view to include new columns ────────────────────────
-- DROP first because CREATE OR REPLACE cannot reorder/insert columns.
DROP VIEW IF EXISTS wine_entries;
CREATE VIEW wine_entries
WITH (security_invoker = true) AS
WITH ranked AS (
    SELECT
        *,
        ROW_NUMBER() OVER (PARTITION BY wine_id ORDER BY created_at DESC) AS rn,
        COUNT(*)     OVER (PARTITION BY wine_id)                          AS log_count
    FROM wine_logs
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
    w.food_pairings,
    w.description,
    w.technical_notes,
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
