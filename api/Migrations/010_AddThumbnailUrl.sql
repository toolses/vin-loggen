-- ── Migration 010: Add thumbnail_url to wine_logs ────────────────────────────
-- Stores a separate 200x200 WebP thumbnail URL for efficient list rendering.

ALTER TABLE wine_logs ADD COLUMN IF NOT EXISTS thumbnail_url TEXT;

-- ── Recreate wine_entries view to include thumbnail_url ──────────────────────
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
    r.thumbnail_url,
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
