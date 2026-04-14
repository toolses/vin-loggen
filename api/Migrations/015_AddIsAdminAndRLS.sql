-- 015_AddIsAdminAndRLS.sql
-- Adds is_admin flag to user_profiles and updates RLS so admins can
-- SELECT and UPDATE all rows while regular users access only their own.

ALTER TABLE user_profiles
    ADD COLUMN IF NOT EXISTS is_admin BOOLEAN NOT NULL DEFAULT FALSE;

-- Helper function that checks admin status WITHOUT going through RLS
-- (SECURITY DEFINER runs as the function owner, bypassing row-level policies).
CREATE OR REPLACE FUNCTION public.is_admin()
RETURNS BOOLEAN
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public
AS $$
    SELECT COALESCE(
        (SELECT is_admin FROM user_profiles WHERE user_id = auth.uid()),
        FALSE
    );
$$;

-- Drop the old catch-all policy so we can replace it with granular ones.
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'Users manage own profile' AND tablename = 'user_profiles') THEN
    DROP POLICY "Users manage own profile" ON user_profiles;
  END IF;
END $$;

-- Also drop the recursive policies if they were already applied.
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'Admins read all profiles' AND tablename = 'user_profiles') THEN
    DROP POLICY "Admins read all profiles" ON user_profiles;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'Admins update all profiles' AND tablename = 'user_profiles') THEN
    DROP POLICY "Admins update all profiles" ON user_profiles;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'Users manage own profile row' AND tablename = 'user_profiles') THEN
    DROP POLICY "Users manage own profile row" ON user_profiles;
  END IF;
END $$;

-- Regular users: full access to their own row
CREATE POLICY "Users manage own profile row" ON user_profiles
    FOR ALL
    USING  (auth.uid() = user_id)
    WITH CHECK (auth.uid() = user_id);

-- Admins: SELECT all rows (uses SECURITY DEFINER function to avoid recursion)
CREATE POLICY "Admins read all profiles" ON user_profiles
    FOR SELECT
    USING (public.is_admin());

-- Admins: UPDATE all rows
CREATE POLICY "Admins update all profiles" ON user_profiles
    FOR UPDATE
    USING (public.is_admin());
