# 🍷 VinLoggen 2026

En moderne, AI-drevet PWA for registrering og vurdering av vin. Bygget for hurtighet, nøyaktighet og minimal driftskostnad.

## 🚀 Teknologi-stabel

| Lag | Teknologi | Beskrivelse |
| :--- | :--- | :--- |
| **Frontend** | Angular 21 | Signals-basert, Zoneless, Tailwind CSS 4 |
| **Backend** | .NET 10 | Azure Functions (Isolated Worker) |
| **Hosting** | Azure Static Web Apps | Gratis hosting for web og API |
| **Database** | Supabase (Postgres) | Database, Autentisering og Blob Storage |
| **AI Vision** | Gemini 2.0 Flash | Ekstrahering av data fra vinetiketter |
| **Integrasjon** | Vinmonopolet API | Berikelse av produktdata og priser |

## 🏗️ Prosjektstruktur

- `/client`: Angular-applikasjonen. En lettvekt PWA optimalisert for mobilbruk.
- `/api`: .NET Azure Functions. Håndterer logikk, AI-oppslag og integrasjoner.
- `/infra`: Bicep/Terraform-filer for oppsett av Azure-ressurser.

## ✨ Kjernefunksjonalitet

- **Quick-Log:** Ta bilde av etiketten -> AI analyserer -> Data fylles ut automatisk.
- **Smart Enrichment:** Matcher automatisk mot Vinmonopolets vareutvalg for nøyaktig info.
- **Personlig Smaksprofil:** Logg notater, terningkast og lokasjon.
- **Offline Support:** Fungerer som en app på mobilen via PWA-funksjonalitet.

## 🛠️ Kom i gang (Lokal utvikling)

### Forutsetninger
- Node.js (nyeste versjon)
- .NET 10 SDK
- Azure Static Web Apps CLI (`npm install -g @azure/static-web-apps-cli`)
- Supabase CLI

### Oppsett
1. Klone repoet: `git clone https://github.com/ditt-brukernavn/vinloggen.git`
2. Installer avhengigheter i `/client`: `npm install`
3. Opprett `.env` filer i både `/client` og `/api` basert på `.env.example`.
4. Start utviklingsmiljøet:
   ```bash
   swa start http://localhost:4200 --api-location ./api
   ```

### Miljøvariabler

**`/client/src/environments/environment.ts`**
```typescript
export const environment = {
  production: false,
  supabaseUrl: 'https://din-prosjekt-ref.supabase.co',
  supabaseAnonKey: 'din-anon-nokkel',
  apiBaseUrl: '/api',
};
```

**`/api/local.settings.json`**
```json
{
  "Values": {
    "SUPABASE_CONNECTION_STRING": "postgresql://postgres:[PASSWORD]@db.[REF].supabase.co:5432/postgres",
    "GEMINI_API_KEY": "din-gemini-api-nokkel"
  }
}
```

## 🗄️ Database-skjema (Supabase SQL)

```sql
CREATE TABLE wines (
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

-- Row Level Security
ALTER TABLE wines ENABLE ROW LEVEL SECURITY;
CREATE POLICY "Public read" ON wines FOR SELECT USING (true);
CREATE POLICY "Auth insert" ON wines FOR INSERT WITH CHECK (auth.role() = 'authenticated');

-- Storage bucket for wine label images
INSERT INTO storage.buckets (id, name, public) VALUES ('wine-labels', 'wine-labels', true);
```

## 📦 Deploy

Azure Static Web Apps henter automatisk kode fra GitHub og bygger:
- **Frontend:** `client/` → `dist/vin-loggen/browser/`
- **API:** `api/` → Azure Functions
