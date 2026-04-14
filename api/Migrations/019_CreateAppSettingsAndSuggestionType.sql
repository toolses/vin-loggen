-- 019: Create app_settings table and add suggestion_type to expert_wine_suggestions

-- Runtime-configurable key-value settings for admin toggles
CREATE TABLE IF NOT EXISTS app_settings (
    key        TEXT PRIMARY KEY,
    value      TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Default expert mode to the new type-based suggestions
INSERT INTO app_settings (key, value)
VALUES ('expert_mode', 'type')
ON CONFLICT (key) DO NOTHING;

-- Distinguish old wine-specific suggestions from new type-based ones
ALTER TABLE expert_wine_suggestions
ADD COLUMN IF NOT EXISTS suggestion_type TEXT NOT NULL DEFAULT 'wine';
