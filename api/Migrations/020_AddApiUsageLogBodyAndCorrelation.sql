-- 020_AddApiUsageLogBodyAndCorrelation.sql
-- Adds request/response body logging and a correlation_id to api_usage_logs.
-- correlation_id ties all API calls that belong to the same user-initiated flow
-- (e.g. a label scan or an expert chat session) so the full pipeline can be traced.

ALTER TABLE api_usage_logs
    ADD COLUMN IF NOT EXISTS request_body   TEXT,
    ADD COLUMN IF NOT EXISTS response_body  TEXT,
    ADD COLUMN IF NOT EXISTS correlation_id UUID;

CREATE INDEX IF NOT EXISTS idx_api_usage_logs_correlation
    ON api_usage_logs (correlation_id)
    WHERE correlation_id IS NOT NULL;
