-- 021: Add observability columns for AI model tracking
ALTER TABLE api_usage_logs ADD COLUMN IF NOT EXISTS used_model TEXT;
ALTER TABLE api_usage_logs ADD COLUMN IF NOT EXISTS total_tokens_used INTEGER;
