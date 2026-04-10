# VinLoggen – Project Instructions

## Overview

VinLoggen is a mobile-first PWA for logging and rating wine. Users photograph a wine label; the image is sent to Gemini 2.0 Flash, which extracts structured wine data. Records are stored in a Supabase Postgres database.

---

## Tech Stack

| Layer | Technology |
|---|---|
| **Frontend** | Angular 21 – Zoneless (`provideZonelessChangeDetection`), Signals, Tailwind CSS 4, PWA (service worker) |
| **Backend** | .NET 10 Minimal API (Kestrel), C# 13 |
| **Data access** | Dapper + Npgsql + `Npgsql.DependencyInjection` |
| **Database** | Supabase (Postgres) |
| **AI** | Gemini 2.0 Flash – wine label extraction |
| **API docs** | `Microsoft.AspNetCore.OpenApi` + Scalar UI (`/scalar/v1`) |
| **Local dev** | Docker Compose (API → `:5000`, client → `:4200`) |
| **Hosting** | Vercel (client) · Render (API) |

---

## Project Structure

```
/
├── INSTRUCTIONS.md
├── docker-compose.yml        # Local dev stack
├── vin-loggen.sln
├── api/                      # .NET 10 Minimal API
│   ├── Program.cs            # DI, middleware, CORS, endpoint registration
│   ├── Dockerfile
│   ├── VinLoggen.Api.csproj
│   ├── Endpoints/            # One static class per resource
│   │   ├── HealthEndpoints.cs
│   │   ├── WineEndpoints.cs
│   │   └── ProcessLabelEndpoints.cs
│   └── Models/
│       └── WineRecord.cs
└── client/                   # Angular 21 app
    ├── src/
    │   ├── app/
    │   │   ├── components/   # Standalone components
    │   │   ├── services/     # Injectable services (HttpClient-based)
    │   │   └── app.routes.ts # Lazy-loaded route config
    │   └── environments/     # environment.ts / environment.prod.ts
    ├── proxy.conf.json        # Dev proxy: /api → localhost:5000
    └── proxy.conf.docker.json # Docker dev proxy: /api → api:8080
```

---

## API Conventions

- All routes are prefixed `/api/`.
- Each resource gets its own endpoint class with a `MapXxxEndpoints(this IEndpointRouteBuilder)` extension method, registered in `Program.cs`.
- Use `TypedResults` (`Results<Ok<T>, ProblemHttpResult>`) for strongly-typed responses.
- Database access via `NpgsqlDataSource` injected directly into handler methods; queries written with Dapper.
- Return `ProblemHttpResult` (RFC 9457) on errors – never throw unhandled exceptions to the client.

---

## Frontend Conventions

- **Zoneless Angular**: no `NgZone`, no `zone.js`. Use Signals and `signal()`/`computed()`/`effect()` for reactivity.
- **Standalone components only** – no NgModules.
- **Tailwind CSS 4** for all styling (no custom SCSS except `styles.css` global resets).
- HTTP calls go through services in `src/app/services/`; components do not call `HttpClient` directly.
- Lazy-load feature routes in `app.routes.ts`.

---

## Environment Variables

### API (`docker-compose.yml` / Render)

| Variable | Required | Description |
|---|---|---|
| `SUPABASE_CONNECTION_STRING` | Yes | Postgres connection string for Supabase |
| `GEMINI_API_KEY` | Yes | Google AI Gemini API key |
| `PORT` | Render only | Injected by Render; overrides Kestrel listen port |
| `CORS_ALLOWED_ORIGINS` | No | Comma-separated extra origins (beyond `*.vercel.app` and `localhost`) |

### Client

Configured via `src/environments/environment.ts` (dev) and `environment.prod.ts` (prod).

---

## Local Development

### Prerequisites

- Docker Desktop
- `.env` file in the repo root (see below)

### Minimum `.env`

```env
SUPABASE_CONNECTION_STRING=postgresql://postgres:[PW]@db.[REF].supabase.co:5432/postgres
GEMINI_API_KEY=sk-...
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
