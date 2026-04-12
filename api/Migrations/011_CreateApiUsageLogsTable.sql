-- 011_CreateApiUsageLogsTable.sql
-- Tracks external API calls (Gemini, wineapi.io, etc.) for admin monitoring.

CREATE TABLE IF NOT EXISTS api_usage_logs (
    id               UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    provider         TEXT          NOT NULL,
    endpoint         TEXT          NOT NULL,
    status_code      INTEGER,
    response_time_ms INTEGER,
    user_id          UUID          REFERENCES auth.users(id) ON DELETE SET NULL,
    created_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_api_usage_logs_provider_created
    ON api_usage_logs (provider, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_api_usage_logs_created
    ON api_usage_logs (created_at DESC);

ALTER TABLE api_usage_logs ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Service role full access"
    ON api_usage_logs
    FOR ALL
    TO service_role
    USING (true)
    WITH CHECK (true);
