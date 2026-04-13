# VinLoggen

En moderne, AI-drevet PWA for registrering og vurdering av vin. Ta bilde av etiketten, la AI analysere den, og bygg din personlige vinlogg.

> **Status:** Alpha — under aktiv utvikling.

## Teknologi

| Lag | Teknologi |
| :--- | :--- |
| **Frontend** | Angular 21 (Zoneless, Signals), Tailwind CSS 4, PWA |
| **Backend** | .NET 10 Minimal API (Kestrel) |
| **Database** | Supabase (Postgres) med Row Level Security |
| **Auth** | Supabase Auth (Google + Apple OAuth) |
| **AI** | Gemini 2.0 Flash — etikett-analyse med multi-bilde-støtte |
| **Vindata** | wineapi.io — berikelse, priser og matparing |
| **Lokasjon** | Google Places API — stedsøk for smakslogg |
| **Lagring** | Supabase Storage — bilder av vinetiketter |
| **Hosting** | Vercel (frontend) · Render (API) |

## Prosjektstruktur

- `/client` — Angular-applikasjonen. En lettvekt PWA optimalisert for mobilbruk.
- `/api` — .NET 10 Minimal API. Håndterer logikk, AI-analyse og integrasjoner.
- `/supabase` — Supabase CLI-konfigurasjon og migrasjoner.

## Kjernefunksjonalitet

- **Quick-Log:** Ta bilde av etiketten → AI analyserer → data fylles ut automatisk.
- **Smart Enrichment:** Matcher automatisk mot Vinmonopolets vareutvalg for nøyaktig info, priser og matanbefalinger.
- **Personlig Smaksprofil:** Logg notater, terningkast, lokasjon og bilder for hver smaking.
- **Deling:** Generer delekort for viner du vil anbefale.
- **Admin-panel:** Dashboard med API-statistikk, vinregister med søk og redigering.
- **Offline Support:** Fungerer som en app på mobilen via PWA-funksjonalitet.

## Kom i gang (lokal utvikling)

### Forutsetninger

- Docker Desktop
- `.env`-fil i repo-roten (se `.env.example` og `api/.env.example`)

### Start

```bash
docker compose up --build
```

- Frontend: http://localhost:4200
- API: http://localhost:5000
- API-dokumentasjon (Scalar): http://localhost:5000/scalar/v1

### Miljøvariabler

Se `INSTRUCTIONS.md` for komplett oversikt over alle miljøvariabler.

## Deploy

- **Frontend** deployes automatisk til Vercel fra `main`-branchen.
- **API** deployes automatisk til Render fra `main`-branchen.
- **Database** hostes på Supabase. Migrasjoner kjøres automatisk via DbUp ved oppstart av API-et.

## Dokumentasjon

- `INSTRUCTIONS.md` — Fullstendig prosjektguide (konvensjoner, struktur, oppsett)
- `CHANGELOG.md` — Endringslogg
