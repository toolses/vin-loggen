# Changelog

All notable changes to VinLoggen are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Admin "Reset Data" tool (danger zone) for clearing all wines, tasting logs, and storage objects during alpha/beta testing
- Agent rules in INSTRUCTIONS.md requiring README.md and CHANGELOG.md updates on every change
- This changelog

### Changed
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
