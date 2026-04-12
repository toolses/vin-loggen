-- 012_UpdateLocationTypeConstraint.sql
-- Replace 'butikk' with 'bar' in location_type CHECK constraint on wine_logs.

-- Migrate any existing 'butikk' entries to 'annet' before changing the constraint
UPDATE wine_logs SET location_type = 'annet' WHERE location_type = 'butikk';

ALTER TABLE wine_logs DROP CONSTRAINT IF EXISTS wine_logs_location_type_check;
ALTER TABLE wine_logs ADD CONSTRAINT wine_logs_location_type_check
    CHECK (location_type IN ('restaurant', 'bar', 'hjemme', 'annet'));
