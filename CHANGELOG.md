# Changelog

All notable changes to VinSomm are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.9.0] - 2026-04-14

### Added
- **Groq-First Performance Engine:** All AI calls now try Groq first for faster inference
- **Groq Chat Provider:** `GroqChatProvider` using Qwen 3 (`qwen/qwen3-32b`) for expert chat, with OpenAI-compatible API
- **Groq Vision (Llama 4 Scout):** `LabelScanService` with `meta-llama/llama-4-scout-17b-16e-instruct` as primary vision model for label scanning
- `LabelScanService` vision fallback chain: Groq (Llama 4 Scout) → Gemini Flash Lite
- Expert chat fallback chain updated: Groq (Qwen 3) → DeepSeek-V3 → Gemini Flash Lite
- Context truncation for Qwen 3's 6K TPM limit: limits catalog wines to 3 and recent tastings to 2 when Groq is primary
- Aggressive 429 handling: Groq rate limit triggers immediate fallback to next provider (no retry)
- Qwen 3 `<think>` block stripping: reasoning traces are removed before returning the answer
- `GroqSettings` configuration with configurable `BaseUrl` (default: `https://api.groq.com/openai`)
- Groq HTTP client registered without standard resilience handler (avoids auto-retry on 429)

### Changed
- Default `AiFallbackSettings.ExpertChatPriority` updated to `["Groq", "DeepSeek", "Gemini"]`
- Default `AiFallbackSettings.LabelScanPriority` updated to `["Groq", "Gemini"]`
- `WineOrchestratorService` now uses `ILabelScanService` instead of direct `IGeminiService` for label scanning
- **AI Observability:** `api_usage_logs` now tracks `used_model` (Q3/L4S/DS/GEM) and `total_tokens_used` per call
- Migration `021_AddModelAndTokensToApiUsageLogs.sql` adds observability columns
- All AI providers (Groq, DeepSeek, Gemini) now report model codes and token counts in `AiChatResult`
- Admin usage endpoints return `usedModel` and `totalTokens` fields
- Admin dashboard shows model badges (color-coded per provider) and token counts in usage tables
- `GROQ_API_KEY` added to docker-compose.yml environment

## [0.8.0] - 2026-04-13

### Added
- **DeepSeek-V3 Integration:** Expert chat now uses DeepSeek-V3 (`deepseek-chat`) as primary AI provider via OpenAI-compatible API
- **AI Provider Fallback Pattern:** `IAiChatProvider` and `IAiVisionProvider` abstractions with `AiProviderChain` orchestrator that iterates through configured priority list on transient failures (503, 429, timeout)
- `DeepSeekChatProvider`, `GeminiChatProvider`, `GeminiVisionProvider` implementations
- `AiFallbackSettings` in config: `ExpertChatPriority: ["DeepSeek", "Gemini"]`, `LabelScanPriority: ["Gemini"]`
- `DeepSeekSettings` with configurable BaseUrl (defaults to `https://api.deepseek.com`)
- `modelUsed` field on `ExpertResponse` — surfaces which AI model actually answered each request
- AI model badge (DS/GEM) shown in expert chat UI next to assistant messages
- DeepSeek HTTP client with standard resilience handler (retry, circuit breaker)
- Fallback logging: every provider switch is logged at Warning level for observability

### Changed
- `ExpertService` refactored to use `AiProviderChain` instead of direct Gemini calls
- Configuration updated with `Integration__DeepSeek` and `Integration__AiFallback` sections

## [0.7.0] - 2026-04-12

### Added
- **Admin User Management & RLS Security:** Full admin UI for browsing and managing user accounts
- `correlation_id` ties all log entries from a single user action (label scan, expert question) together for end-to-end tracing
- `user_id` in `api_usage_logs` now correctly records the initiating user for Gemini, WineAPI, DeepSeek, and expert AI calls
- Migration `020_AddApiUsageLogBodyAndCorrelation.sql`: adds 3 columns and a `WHERE correlation_id IS NOT NULL` partial index

- **Expert Conversation Persistence:** All expert conversations are now saved to the database with session history
- `expert_sessions`, `expert_messages`, `expert_wine_suggestions` tables with RLS
- `GET /api/expert/sessions` — paginated session history list
- `GET /api/expert/sessions/{id}` — full session with messages and wine suggestions
- `DELETE /api/expert/sessions/{id}` — delete a session
- `PATCH /api/expert/suggestions/{id}/feedback` — thumbs up/down on wine suggestions
- Session history panel in Expert UI with browsable past conversations
- Feedback buttons (thumbs up/down) on each wine suggestion card
- Past session read-only view with "Start ny samtale" CTA
- `sessionId` and `wineSuggestionIds` returned from `/api/expert/ask` for session continuity
- **Admin User Management:** Full admin UI for browsing and managing user accounts
- `GET /api/admin/users` endpoint returning all profiles with email, display name, tier, activity, and admin flag
- `PATCH /api/admin/users/{id}/tier` endpoint for toggling subscription tier (free/pro)
- `AdminUserListComponent` with server-side search, skeleton loading, tier dropdown, and activity badges
- Database migration `015_AddIsAdminAndRLS.sql`: adds `is_admin` column to `user_profiles` and granular RLS policies (admins can read/update all rows, users only their own)
- "Brukere" navigation item in admin sidebar
- Toast notifications on tier update success/failure
- **Expert Agent ("Eksperten"):** AI-drevet vinkelner-chat for Pro-brukere med RAG-pipeline mot global vinkatalog, smaksprofil og siste smakslogg
- `POST /api/expert/ask` endpoint med fuzzy vinsoek, kontekstbygging og Gemini-integrasjon
- WineAPI `/identify/text` integrasjon i Eksperten for anbefalinger fra global vinkatalog med rating og matparing
- ExpertComponent med personlig hilsen, AI-kvoteindikator, hurtigvalg-chips og chat-grensesnitt
- Dynamisk navigasjon: Pro-brukere ser "Eksperten" i bunnavigasjon og dashboard-CTA
- **Hard daily API limits:** Gemini (500/day) and WineAPI (100/day) via shared `ApiQuotaGuard`, configurable via `Integration__GeminiMaxDailyRequests` / `Integration__WineApiMaxDailyRequests`

### Fixed
- Wine API 401 errors: corrected default auth header from `Authorization: Bearer` to `X-API-Key` to match wineapi.io docs
- Share image on iOS Safari: replaced canvas-based image inlining with cache-busted fetch to avoid tainted CORS cache
- Date picker overflowing viewport on iOS Safari in wine editor

### Added
- Google Places API integration for location search and details in tasting log
- Admin "Reset Data" tool (danger zone) for clearing all wines, tasting logs, and storage objects during alpha/beta testing
- Agent rules in INSTRUCTIONS.md requiring README.md and CHANGELOG.md updates on every change

### Changed
- Refactored icon generation script to read from existing SVG file instead of inline definition
- Updated INSTRUCTIONS.md to reflect current project status (database schema, admin panel, all environment variables, full project structure)
- Updated README.md to match actual tech stack and hosting setup

## [0.6.0] - 2026-04-11

### Added
- Manual wine search and suggestion handling in wine editor

## [0.5.0] - 2026-04-10

### Fixed
- Share card dimensions for better scaling in share preview

### Added
- `StringArrayTypeHandler` for PostgreSQL TEXT[] support in Dapper
- Updated `AdminWineDetail` model with array field support

### Fixed
- Bottom nav no longer covers page content on mobile

## [0.4.0] - 2026-04-09

### Added
- Multi-image support for wine label analysis in scanner
- Auth guard on dashboard route
- Pro enrichment columns (food_pairings, description, technical_notes) on wines table
- `pg_trgm` extension and trigram indexes for fuzzy search
- Enhanced wine management with enrichment fields and API response caching

## [0.3.0] - 2026-04-08

### Added
- User profile component with taste profile display
- Wine sharing functionality (share card generation)
- PostCSS configuration and Google Fonts integration

## [0.2.0] - 2026-04-07

### Added
- User authentication (Supabase Auth with Google/Apple OAuth)
- Wine logs (per-user tasting events with ratings, notes, location)
- User profiles and taste profile analysis
- Admin panel (dashboard, wine registry, wine editor)
- API usage logging and monitoring
- Wine label image upload to Supabase Storage
- Pro/freemium quota system
- Location tracking for tastings
- WineAPI (Vinmonopolet) integration for wine enrichment

### Changed
- Migrated from Azure Static Web Apps to Vercel (client) + Render (API)
- Split monolithic wines table into wines (catalogue) + wine_logs (per-user events)

## [0.1.0] - 2026-04-06

### Added
- Initial project bootstrap: Angular 21 PWA + .NET 10 Minimal API
- Gemini 2.0 Flash integration for wine label extraction
- Basic wine CRUD with Supabase Postgres
- Docker Compose local dev setup
- Scalar API documentation
