CREATE TABLE IF NOT EXISTS wines (
  id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  name        TEXT NOT NULL,
  producer    TEXT NOT NULL DEFAULT '',
  vintage     INTEGER,
  type        TEXT NOT NULL DEFAULT 'Rødvin',
  country     TEXT,
  region      TEXT,
  rating      NUMERIC(2,1),
  notes       TEXT,
  image_url   TEXT,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE wines ENABLE ROW LEVEL SECURITY;

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'Public read' AND tablename = 'wines') THEN
    CREATE POLICY "Public read" ON wines FOR SELECT USING (true);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'Auth insert' AND tablename = 'wines') THEN
    CREATE POLICY "Auth insert" ON wines FOR INSERT WITH CHECK (auth.role() = 'authenticated');
  END IF;
END $$;
