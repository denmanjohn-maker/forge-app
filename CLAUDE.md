# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

mtg-forge is a Magic: The Gathering deck generator powered by Claude AI. It uses ASP.NET 10 with MongoDB for persistence and a vanilla JavaScript SPA frontend served from `wwwroot/index.html`.

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
- Docker multi-stage build: `sdk:10.0` → build, `aspnet:10.0` → runtime, exposed on port 5000.

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **forge-app** (1308 symbols, 2731 relationships, 39 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> If any GitNexus tool warns the index is stale, run `npx gitnexus analyze` in terminal first.

## Always Do

- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `gitnexus_impact({target: "symbolName", direction: "upstream"})` and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `gitnexus_detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `gitnexus_query({query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `gitnexus_context({name: "symbolName"})`.

## Never Do

- NEVER edit a function, class, or method without first running `gitnexus_impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `gitnexus_rename` which understands the call graph.
- NEVER commit changes without running `gitnexus_detect_changes()` to check affected scope.

## Resources

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/forge-app/context` | Codebase overview, check index freshness |
| `gitnexus://repo/forge-app/clusters` | All functional areas |
| `gitnexus://repo/forge-app/processes` | All execution flows |
| `gitnexus://repo/forge-app/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->
