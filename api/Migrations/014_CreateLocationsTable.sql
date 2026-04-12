-- 014_CreateLocationsTable.sql
-- Cache for resolved Google Places results to minimize API calls.

CREATE TABLE IF NOT EXISTS locations (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    google_place_id TEXT NOT NULL UNIQUE,
    name            TEXT NOT NULL,
    address         TEXT,
    lat             DOUBLE PRECISION NOT NULL,
    lng             DOUBLE PRECISION NOT NULL,
    types           TEXT[],
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
