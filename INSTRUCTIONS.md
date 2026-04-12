# VinLoggen – Project Instructions

> **Status:** Alpha (v0.x) — actively developed, not yet production-ready.

## Overview

VinLoggen is a mobile-first PWA for logging and rating wine. Users photograph a wine label; the image is sent to Gemini 2.0 Flash, which extracts structured wine data. The wine is then enriched via the WineAPI (Vinmonopolet data). Records are stored in a Supabase Postgres database. Users can build a personal tasting log with ratings, notes, location, and photos.

---

## Tech Stack

| Layer | Technology |
|---|---|
| **Frontend** | Angular 21 – Zoneless (`provideZonelessChangeDetection`), Signals, Tailwind CSS 4, PWA (service worker) |
| **Backend** | .NET 10 Minimal API (Kestrel), C# 13 |
| **Data access** | Dapper + Npgsql + `Npgsql.DependencyInjection` |
| **Database** | Supabase (Postgres) with Row Level Security |
| **Auth** | Supabase Auth (JWT), Google + Apple OAuth |
| **AI** | Gemini 2.0 Flash – wine label extraction (multi-image support) |
| **Wine data** | WineAPI (Vinmonopolet) – enrichment, search, and pricing |
| **Storage** | Supabase Storage – `wine-labels` bucket for label images |
| **API docs** | `Microsoft.AspNetCore.OpenApi` + Scalar UI (`/scalar/v1`) |
| **Local dev** | Docker Compose (API → `:5000`, client → `:4200`) |
| **Hosting** | Vercel (client) · Render (API) |

---

## Project Structure

```
/
├── INSTRUCTIONS.md
├── CHANGELOG.md
├── README.md
├── docker-compose.yml            # Local dev stack
├── vin-loggen.sln
├── api/                          # .NET 10 Minimal API
│   ├── Program.cs                # DI, middleware, CORS, auth, endpoint registration
│   ├── Dockerfile
│   ├── VinLoggen.Api.csproj
│   ├── Configuration/
│   │   ├── AdminSettings.cs      # Admin user allowlist
│   │   └── StringArrayTypeHandler.cs # Dapper TEXT[] mapping
│   ├── Endpoints/                # One static class per resource
│   │   ├── HealthEndpoints.cs
│   │   ├── WineEndpoints.cs
│   │   ├── WineLogsEndpoints.cs
│   │   ├── ProcessLabelEndpoints.cs
│   │   ├── WineAnalyzeEndpoints.cs
│   │   ├── TasteProfileEndpoints.cs
│   │   ├── AdminAuthEndpoints.cs
│   │   ├── AdminWineEndpoints.cs
│   │   ├── AdminUsageEndpoints.cs
│   │   └── AdminResetEndpoints.cs  # Alpha/beta only – data reset
│   ├── Services/
│   │   ├── GeminiService.cs      # Gemini AI label extraction
│   │   ├── WineApiService.cs     # WineAPI (Vinmonopolet) integration
│   │   ├── WineOrchestratorService.cs # Coordinates AI + enrichment
│   │   └── ProUsageService.cs    # Freemium quota tracking
│   ├── Models/
│   │   ├── WineRecord.cs         # wines table
│   │   ├── WineLogRecord.cs      # wine_logs table
│   │   ├── UserProfile.cs        # user_profiles table
│   │   ├── AdminWineModels.cs    # Admin DTOs
│   │   └── ApiResult.cs          # Generic response wrapper
│   └── Migrations/               # DbUp SQL migrations (001–011)
└── client/                       # Angular 21 app
    ├── src/
    │   ├── app/
    │   │   ├── components/       # Standalone components
    │   │   │   ├── admin/        # Admin panel (dashboard, wine list, wine editor)
    │   │   │   ├── scanner/      # Camera/label scanning
    │   │   │   ├── wine-editor/  # Wine editing form
    │   │   │   ├── dashboard/    # User wine log
    │   │   │   ├── profile/      # User profile & taste profile
    │   │   │   └── navigation/   # Bottom nav bar
    │   │   ├── guards/           # Auth guard, admin guard
    │   │   ├── services/         # Injectable services (HttpClient-based)
    │   │   └── app.routes.ts     # Lazy-loaded route config
    │   └── environments/         # environment.ts / environment.prod.ts
    ├── proxy.conf.json           # Dev proxy: /api → localhost:5000
    └── proxy.conf.docker.json    # Docker dev proxy: /api → api:8080
```

---

## Database Schema

The database has evolved through 11 DbUp migrations. Current tables:

| Table | Description |
|---|---|
| `wines` | Master wine catalogue (deduplicated). UNIQUE on `(producer, name, vintage)`. |
| `wine_logs` | Per-user tasting events with rating, notes, images, location. FK to `wines`. |
| `user_profiles` | User settings, taste profile JSON, subscription tier, quota tracking. |
| `api_usage_logs` | External API call tracking (provider, endpoint, status, response time). |

**Views:** `wine_entries` (joins wines + latest wine_logs per user per wine, security_invoker for RLS).

**Storage:** `wine-labels` bucket (public read, authenticated upload).

**Extensions:** `pg_trgm` for fuzzy text search on wines.

All tables have Row Level Security enabled. Migrations are embedded in the .NET assembly and run via DbUp at startup.

---

## API Conventions

- All routes are prefixed `/api/`.
- Each resource gets its own endpoint class with a `MapXxxEndpoints(this IEndpointRouteBuilder)` extension method, registered in `Program.cs`.
- Use `TypedResults` (`Results<Ok<T>, ProblemHttpResult>`) for strongly-typed responses.
- Database access via `NpgsqlDataSource` injected directly into handler methods; queries written with Dapper.
- Return `ProblemHttpResult` (RFC 9457) on errors – never throw unhandled exceptions to the client.
- Admin endpoints use the `"AdminOnly"` authorization policy (env-var allowlist of Supabase user UUIDs).

---

## Frontend Conventions

- **Zoneless Angular**: no `NgZone`, no `zone.js`. Use Signals and `signal()`/`computed()`/`effect()` for reactivity.
- **Standalone components only** – no NgModules.
- **Tailwind CSS 4** for all styling (no custom SCSS except `styles.css` global resets).
- HTTP calls go through services in `src/app/services/`; components do not call `HttpClient` directly.
- Lazy-load feature routes in `app.routes.ts`.

---

## Admin Panel

The admin section (`/admin`) is protected by an env-var allowlist of Supabase user UUIDs (`ADMIN_USER_IDS`).

**Features:**
- Dashboard with wine count and API usage stats
- Wine registry with search, filter, pagination, and inline editing
- Data reset tool (alpha/beta only) for clearing wines, logs, and storage

---

## Environment Variables

### API (`docker-compose.yml` / Render)

| Variable | Required | Description |
|---|---|---|
| `SUPABASE_CONNECTION_STRING` | Yes | Postgres connection string for Supabase |
| `SUPABASE_URL` | Yes | Supabase project URL (for JWT auth) |
| `GEMINI_API_KEY` | Yes | Google AI Gemini API key |
| `WINE_API_KEY` | Yes | WineAPI key for Vinmonopolet data |
| `ADMIN_USER_IDS` | No | Comma-separated Supabase user UUIDs for admin access |
| `MAPBOX_TOKEN` | No | Mapbox token for map display and reverse geocoding |
| `GOOGLE_PLACES_API_KEY` | No | Google Places API key for location autocomplete |
| `PORT` | Render only | Injected by Render; overrides Kestrel listen port |
| `CORS_ALLOWED_ORIGINS` | No | Comma-separated extra origins (beyond `*.vercel.app` and `localhost`) |

### Client

Configured via `src/environments/environment.ts` (dev) and `environment.prod.ts` (prod). Build-time variables set via `scripts/set-env.mjs`.

---

## Local Development

### Prerequisites

- Docker Desktop
- `.env` file in the repo root (see below)

### Minimum `.env`

```env
SUPABASE_CONNECTION_STRING=postgresql://postgres:[PW]@db.[REF].supabase.co:5432/postgres
SUPABASE_URL=https://[REF].supabase.co
GEMINI_API_KEY=...
WINE_API_KEY=...
```

### Start

```bash
docker compose up --build
```

- API: http://localhost:5000
- API docs (Scalar): http://localhost:5000/scalar/v1
- Client: http://localhost:4200

### Rebuild only the API

```bash
docker compose up --build api
```

---

## Security Notes

- The API container runs as a non-root user (`appuser`, UID 1001).
- CORS is restricted to `localhost`, `*.vercel.app`, and any extra origins in `CORS_ALLOWED_ORIGINS`.
- Never commit `.env` or secrets; the `.env` file is gitignored.
- Validate all inputs at the API boundary before passing to Npgsql/Dapper.
- All database tables use Row Level Security (RLS) – users can only access their own data.
- Admin access is controlled via a server-side allowlist, not database flags.

---

## Agent Rules

When working on this project as an AI coding agent, follow these rules:

1. **Always update `README.md`** when features, setup instructions, tech stack, or project structure have meaningfully changed.
2. **Always update `CHANGELOG.md`** with a summary of what was added, changed, or fixed. Follow the Keep a Changelog format.
3. **Keep `INSTRUCTIONS.md` in sync** – if you add new endpoints, services, models, migrations, or environment variables, update the relevant sections here.
4. **Do not skip documentation** – documentation drift causes confusion for future agents and developers. Treat doc updates as part of the task, not an afterthought.
