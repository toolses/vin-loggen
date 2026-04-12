-- 013_CreateDataCorrectionsTable.sql
-- Tracks user corrections to AI/API-provided wine data for feedback and audit.

CREATE TABLE IF NOT EXISTS data_corrections (
    id              UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id         UUID         NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    wine_id         UUID         REFERENCES wines(id) ON DELETE SET NULL,
    source          TEXT         NOT NULL CHECK (source IN ('gemini', 'wineapi', 'manual')),
    original_data   JSONB        NOT NULL,
    corrected_data  JSONB        NOT NULL,
    comment         TEXT,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_data_corrections_user_id    ON data_corrections (user_id);
CREATE INDEX IF NOT EXISTS idx_data_corrections_created_at ON data_corrections (created_at DESC);

ALTER TABLE data_corrections ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Users insert own data_corrections"
    ON data_corrections FOR INSERT
    WITH CHECK (auth.uid() = user_id);

CREATE POLICY "Users read own data_corrections"
    ON data_corrections FOR SELECT
    USING (auth.uid() = user_id);
