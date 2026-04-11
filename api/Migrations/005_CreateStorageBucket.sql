-- ============================================================================
-- 005_CreateStorageBucket.sql
-- Creates the wine-labels storage bucket and RLS policies.
-- ============================================================================

-- Create the bucket (skip if it already exists)
INSERT INTO storage.buckets (id, name, public)
VALUES ('wine-labels', 'wine-labels', true)
ON CONFLICT (id) DO NOTHING;

-- Allow authenticated users to upload files
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies
    WHERE schemaname = 'storage' AND tablename = 'objects'
      AND policyname = 'Authenticated users can upload labels'
  ) THEN
    CREATE POLICY "Authenticated users can upload labels"
    ON storage.objects FOR INSERT
    TO authenticated
    WITH CHECK (bucket_id = 'wine-labels');
  END IF;
END $$;

-- Allow anyone to read/download labels (public bucket)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies
    WHERE schemaname = 'storage' AND tablename = 'objects'
      AND policyname = 'Public read access for labels'
  ) THEN
    CREATE POLICY "Public read access for labels"
    ON storage.objects FOR SELECT
    TO public
    USING (bucket_id = 'wine-labels');
  END IF;
END $$;
