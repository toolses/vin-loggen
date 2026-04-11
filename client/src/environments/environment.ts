// Supabase anon keys are intentionally public – they are protected by Row Level Security (RLS).
// The Gemini API key lives server-side only (api/local.settings.json).
export const environment = {
  production: false,
  supabaseUrl: 'https://quvkgyiybiyfwnjpembr.supabase.co',
  supabaseAnonKey: 'sb_publishable_HOSKA-LisfvKIj_Z61Wu1g_Ag8DnmPP',
  apiBaseUrl: '/api',
  mapboxToken: '',
} as const;
