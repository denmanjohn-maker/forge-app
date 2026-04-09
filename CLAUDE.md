# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MTG Deck Forge is a Magic: The Gathering deck generator powered by AI. It uses .NET 10 ASP.NET Core with MongoDB for deck storage, PostgreSQL for Identity + pricing data, and a vanilla JavaScript SPA frontend served from `wwwroot/index.html`.

## Build & Run Commands

```bash
# Build
dotnet build MtgDeckForge.sln

# Run locally (requires MongoDB + PostgreSQL — use docker-compose-local.yml)
cd MtgDeckForge.Api && dotnet run

# Local dev infrastructure (MongoDB :27018, PostgreSQL :5433, Prometheus :9090, Grafana :3000)
docker compose -f docker-compose-local.yml up -d

# Tests
dotnet test MtgDeckForge.sln

# Single test
dotnet test MtgDeckForge.Tests --filter "FullyQualifiedName~ClaudeServiceTests"
```

Local dev: API on `http://localhost:5001`, Scalar API docs at `/scalar/v1`.

## Architecture

```
Browser (wwwroot/index.html — vanilla JS SPA, ~2000 lines)
    │
    ▼
.NET 10 ASP.NET Core API  (Razor Pages + REST Controllers)
    ├──► IDeckGenerationService  ──► ClaudeService        (LlmProvider=Claude — Anthropic API)
    │                             └► RagPipelineService   (LlmProvider=Rag — mtg-forge-local + Qdrant + Ollama)
    ├──► DeckService      → MongoDB (decks collection)
    ├──► PricingService   → PostgreSQL (MTGJSON/TCGPlayer price cache)
    ├──► ScryfallService  → Scryfall API (card enrichment, image lookup)
    └──► AuthService      → PostgreSQL (ASP.NET Identity)
```

**LLM Provider toggle** — set `"LlmProvider"` in `appsettings.json` (or env var):
- `"Rag"` (default) → calls `mtg-forge-local` at `RagPipeline__BaseUrl` via `RagPipelineService`
- `"Claude"` → calls Anthropic API via `ClaudeService`

- **Single-page frontend** (`wwwroot/index.html`): ~2000-line vanilla JS/HTML/CSS file with MTG-themed UI. Uses Scryfall API for card images. No build tooling or framework.
- **DecksController**: REST API for deck CRUD, AI generation, AI analysis, CSV import/export (supports moxfield, archidekt, deckbox, deckstats formats). Rate-limited to 20 generations per user per 24 hours.
- **ClaudeService**: Calls Anthropic Messages API directly via HttpClient (not the SDK). Uses `claude-sonnet-4-20250514` with 16384 max tokens.
- **RagPipelineService**: Calls `mtg-forge-local` for deck generation (Qdrant pre-filters cards by price + legality) and Ollama directly for analysis.
- **DeckService**: MongoDB CRUD with `MongoDB.Driver`. Collection: `decks` in `mtgdeckforge` database. Singleton.
- **PricingService**: Looks up card prices from PostgreSQL MTGJSON cache. Scoped.
- **MtgJsonPricingImportService**: Bulk-imports MTGJSON pricing daily into PostgreSQL.
- **AuthService**: JWT generation + password hashing (BCrypt).
- **UserService**: MongoDB user and group CRUD.

## Configuration

Environment variables (via `.env` or system env):
- `LlmProvider` — `Rag` (default) or `Claude`
- `ANTHROPIC_API_KEY` — required when `LlmProvider=Claude`
- `DATABASE_URL` — PostgreSQL URI (Railway format; auto-converted to Npgsql in `Program.cs`)
- `JWT_SECRET` — JWT signing key (min 32 chars)
- `ADMIN_PASSWORD` — seeded admin account password
- `MongoDb__ConnectionString`, `MongoDb__DatabaseName`, `MongoDb__DecksCollectionName`
- `RagPipeline__BaseUrl`, `RagPipeline__OllamaUrl`, `RagPipeline__Model`
- `ClaudeApi__Model`, `ClaudeApi__MaxTokens`
- `CORS_ALLOWED_ORIGINS` — comma-separated allowed origins (wide-open in development)

Production uses Railway environment variable injection.

## Key Endpoints

- `POST /api/auth/login` — login, returns JWT
- `POST /api/auth/register` — register user (Admin only)
- `GET  /api/auth/me` — current user profile
- `GET  /api/decks` — paginated deck list (`?name=&color=&format=&powerLevel=&skip=&limit=`)
- `POST /api/decks/generate` — AI deck generation (rate-limited: 20/24h)
- `PATCH /api/decks/{id}` — update deck
- `POST /api/decks/{id}/copy` — duplicate deck
- `POST /api/decks/{id}/analyze` — AI deck analysis
- `DELETE /api/decks/{id}` — delete deck
- `GET  /api/decks/{id}/export/csv?format=moxfield` — export (moxfield/archidekt/deckbox/deckstats)
- `POST /api/decks/import/csv` — import with auto-format detection
- `GET  /api/pricing/search?q=` — card search via Scryfall
- `GET  /api/pricing/lookup?cardName=` — full price detail (local DB + Scryfall)
- `POST /api/pricing/refresh` — trigger MTGJSON import (Admin)
- `GET  /api/groups` — group management (Admin only)
- `GET  /healthz` — health check
- `GET  /api/version` — build version
- `GET  /metrics` — Prometheus scrape (internal only)
- `GET  /logging` — recent structured logs (internal only)

## Notes

- Tests are in `MtgDeckForge.Tests`: `ClaudeServiceTests`, `DecksControllerCsvHelpersTests`, `ScryfallServiceTests`.
- CORS is wide open in development; set `CORS_ALLOWED_ORIGINS` env var for production.
- The frontend uses three Google Fonts: Cinzel, Crimson Text, MedievalSharp.
- Docker multi-stage build: `sdk:10.0` → build, `aspnet:10.0` → runtime, exposed on port 5000.
- Deployed to Railway (staging: `staging.bensmagicforge.app`). Push to `staging` branch; merge to `main` for production.
