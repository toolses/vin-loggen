-- 016_FixAdminRLSRecursion.sql
-- Fixes infinite recursion in admin RLS policies on user_profiles.
-- The original policies queried user_profiles directly, which triggered
-- the same RLS check again. This migration creates a SECURITY DEFINER
-- function that bypasses RLS for the admin check, then recreates the policies.

-- Helper function that checks admin status WITHOUT going through RLS
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

-- Drop the broken recursive policies
DROP POLICY IF EXISTS "Admins read all profiles" ON user_profiles;
DROP POLICY IF EXISTS "Admins update all profiles" ON user_profiles;

-- Recreate using the SECURITY DEFINER function (no recursion)
CREATE POLICY "Admins read all profiles" ON user_profiles
    FOR SELECT
    USING (public.is_admin());

CREATE POLICY "Admins update all profiles" ON user_profiles
    FOR UPDATE
    USING (public.is_admin());
