# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

mtg-forge is a Magic: The Gathering deck generator powered by Claude AI. It uses ASP.NET 8 with MongoDB for persistence and a vanilla JavaScript SPA frontend served from `wwwroot/index.html`.

## Build & Run Commands

```bash
# Build
dotnet build mtg-forge.sln

# Run locally (requires MongoDB and ANTHROPIC_API_KEY env var)
cd mtg-forge.Api && dotnet run

# Local dev with Docker (includes MongoDB)
docker compose -f docker-compose-local.yml up -d --build

# Deploy to AWS ECS
export AWS_REGION=us-east-1 AWS_ACCOUNT_ID=<account-id>
./deploy/push-to-ecr.sh
```

Local dev: API on `http://localhost:5001`, MongoDB on port 27018. Swagger at `/swagger`.

## Architecture

```
Frontend (wwwroot/index.html) → DecksController → ClaudeService → Anthropic Messages API
                                       ↓
                                  DeckService → MongoDB
```

- **Single-page frontend** (`wwwroot/index.html`): ~2000-line vanilla JS/HTML/CSS file with MTG-themed UI. Uses Scryfall API for card images. No build tooling or framework.
- **DecksController**: REST API for deck CRUD, AI generation, AI analysis, CSV import/export (supports moxfield, archidekt, deckbox, deckstats formats).
- **ClaudeService**: Calls Anthropic Messages API directly via HttpClient (not the SDK). Uses `claude-sonnet-4-20250514` with 8192 max tokens. Generates 100-card Commander decks as JSON.
- **DeckService**: MongoDB CRUD with `MongoDB.Driver`. Collection: `decks` in `mtgdeckforge` database.
- **Models** (`Models/`): `DeckConfiguration` (main document), `CardEntry`, `DeckGenerationRequest`, `DeckAnalysis`, settings POCOs.

## Configuration

Environment variables (via `.env` or system env):
- `ANTHROPIC_API_KEY` — required for AI features
- `MongoDb__ConnectionString`, `MongoDb__DatabaseName`, `MongoDb__DecksCollectionName`
- `ClaudeApi__Model`, `ClaudeApi__MaxTokens`

Production uses AWS Secrets Manager for `ClaudeApi__ApiKey` and `MongoDb__ConnectionString`.

## Key Endpoints

- `POST /api/decks/generate` — AI deck generation
- `POST /api/decks/{id}/analyze` — AI deck analysis
- `GET /api/decks/{id}/export/csv?format=moxfield` — export (moxfield/archidekt/deckbox/deckstats)
- `POST /api/decks/import/csv` — import with auto-format detection
- `GET /healthz` — health check

## Notes

- No test project exists yet.
- CORS is wide open (AllowAny) — development configuration.
- The frontend uses three Google Fonts: Cinzel, Crimson Text, MedievalSharp.
- Docker multi-stage build: `sdk:8.0` → build, `aspnet:8.0` → runtime, exposed on port 5000.
