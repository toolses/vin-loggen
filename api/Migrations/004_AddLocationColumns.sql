-- ============================================================================
-- 003_AddLocationColumns.sql
-- Adds location tracking to wines and home address to user profiles.
-- ============================================================================

-- 1. Location columns on wines
ALTER TABLE wines ADD COLUMN IF NOT EXISTS location_name TEXT;
ALTER TABLE wines ADD COLUMN IF NOT EXISTS location_lat DOUBLE PRECISION;
ALTER TABLE wines ADD COLUMN IF NOT EXISTS location_lng DOUBLE PRECISION;
ALTER TABLE wines ADD COLUMN IF NOT EXISTS location_type TEXT
  CHECK (location_type IN ('restaurant', 'butikk', 'hjemme', 'annet'));

-- Partial index for spatial queries (only rows with coordinates)
CREATE INDEX IF NOT EXISTS idx_wines_location
  ON wines(location_lat, location_lng) WHERE location_lat IS NOT NULL;

-- 2. Home address on user profiles
ALTER TABLE user_profiles ADD COLUMN IF NOT EXISTS home_address_name TEXT;
ALTER TABLE user_profiles ADD COLUMN IF NOT EXISTS home_address_lat DOUBLE PRECISION;
ALTER TABLE user_profiles ADD COLUMN IF NOT EXISTS home_address_lng DOUBLE PRECISION;
