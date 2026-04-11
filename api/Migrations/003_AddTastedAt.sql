-- Add tasting date to wines table
ALTER TABLE wines ADD COLUMN IF NOT EXISTS tasted_at DATE;
