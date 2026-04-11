INSERT INTO storage.buckets (id, name, public)
VALUES ('wine-labels', 'wine-labels', true)
ON CONFLICT (id) DO NOTHING;
