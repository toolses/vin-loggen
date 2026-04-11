-- 007_AddProQuotaColumns.sql
-- Adds freemium / subscription quota tracking to user_profiles.
-- subscription_tier: 'free' | 'pro'
-- pro_scans_today / last_pro_scan_date: rolling daily counter for free-tier Pro features.

ALTER TABLE user_profiles
    ADD COLUMN IF NOT EXISTS subscription_tier   TEXT NOT NULL DEFAULT 'free';

ALTER TABLE user_profiles
    ADD COLUMN IF NOT EXISTS pro_scans_today     INT  NOT NULL DEFAULT 0;

ALTER TABLE user_profiles
    ADD COLUMN IF NOT EXISTS last_pro_scan_date  DATE NOT NULL DEFAULT CURRENT_DATE;

-- Validate tier values; extend this check when new tiers are introduced.
ALTER TABLE user_profiles
    ADD CONSTRAINT IF NOT EXISTS user_profiles_tier_check
    CHECK (subscription_tier IN ('free', 'pro'));
