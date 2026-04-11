-- ============================================================================
-- 002_AddUserIdAndProfiles.sql
-- Adds per-user ownership to wines and creates the user_profiles table.
-- ============================================================================

-- ── 1. Add user_id column to wines ──────────────────────────────────────────
ALTER TABLE wines ADD COLUMN IF NOT EXISTS user_id UUID REFERENCES auth.users(id);

-- Create index for efficient per-user queries
CREATE INDEX IF NOT EXISTS idx_wines_user_id ON wines(user_id);

-- ── 2. Replace RLS policies on wines ────────────────────────────────────────
-- Drop old overly-permissive policies
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'Public read' AND tablename = 'wines') THEN
    DROP POLICY "Public read" ON wines;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'Auth insert' AND tablename = 'wines') THEN
    DROP POLICY "Auth insert" ON wines;
  END IF;
END $$;

-- Per-user policies
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'Users read own wines' AND tablename = 'wines') THEN
    CREATE POLICY "Users read own wines" ON wines FOR SELECT USING (auth.uid() = user_id);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'Users insert own wines' AND tablename = 'wines') THEN
    CREATE POLICY "Users insert own wines" ON wines FOR INSERT WITH CHECK (auth.uid() = user_id);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'Users update own wines' AND tablename = 'wines') THEN
    CREATE POLICY "Users update own wines" ON wines FOR UPDATE USING (auth.uid() = user_id);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'Users delete own wines' AND tablename = 'wines') THEN
    CREATE POLICY "Users delete own wines" ON wines FOR DELETE USING (auth.uid() = user_id);
  END IF;
END $$;

-- Temporary migration-period policy: allow reading orphaned wines (no user_id)
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'Read orphaned wines' AND tablename = 'wines') THEN
    CREATE POLICY "Read orphaned wines" ON wines FOR SELECT USING (user_id IS NULL);
  END IF;
END $$;

-- ── 3. Create user_profiles table ───────────────────────────────────────────
CREATE TABLE IF NOT EXISTS user_profiles (
  user_id                UUID PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
  taste_profile_json     JSONB,
  wines_at_last_analysis INTEGER NOT NULL DEFAULT 0,
  last_analysis_at       TIMESTAMPTZ,
  created_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at             TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE user_profiles ENABLE ROW LEVEL SECURITY;

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'Users manage own profile' AND tablename = 'user_profiles') THEN
    CREATE POLICY "Users manage own profile" ON user_profiles FOR ALL USING (auth.uid() = user_id) WITH CHECK (auth.uid() = user_id);
  END IF;
END $$;
