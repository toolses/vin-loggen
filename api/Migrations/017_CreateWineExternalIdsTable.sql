-- 017_CreateWineExternalIdsTable.sql
-- Creates a mapping table for external wine IDs from multiple sources
-- (WineAPI, Vivino, Vinmonopolet, etc.).
-- Replaces the single-source wines.external_source_id column
-- (kept for backwards compatibility but new code writes to both).

-- ── 1. Create mapping table ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS wine_external_ids (
    id          UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    wine_id     UUID         NOT NULL REFERENCES wines(id) ON DELETE CASCADE,
    source      TEXT         NOT NULL,   -- e.g. 'wineapi', 'vivino', 'vinmonopolet'
    external_id TEXT         NOT NULL,   -- the ID in the external system
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    UNIQUE (wine_id, source)             -- one ID per source per wine
);

-- Fast lookup: "find our wine for WineAPI ID X"
CREATE INDEX IF NOT EXISTS idx_wine_external_ids_source_ext
    ON wine_external_ids (source, external_id);

-- ── 2. Backfill from existing external_source_id column ─────────────────────
INSERT INTO wine_external_ids (wine_id, source, external_id)
SELECT id, 'wineapi', external_source_id
FROM wines
WHERE external_source_id IS NOT NULL
ON CONFLICT DO NOTHING;
