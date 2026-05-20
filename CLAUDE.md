# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> **AI provider:** Deck generation uses **DeepInfra** (`meta-llama/Llama-3.3-70B-Instruct`) via the **forge-ai-api** RAG pipeline — not Anthropic/Claude. `ClaudeService` exists in the codebase but is not the active provider.

## Project Overview

mtg-forge is a Magic: The Gathering deck management and AI generation platform. It uses ASP.NET 10 with MongoDB and PostgreSQL for persistence, and a vanilla JavaScript SPA frontend served from `wwwroot/index.html`. AI deck generation is delegated to the companion **forge-ai-api** service (RAG pipeline using Qdrant + DeepInfra).

## Companion repository

The RAG pipeline service is in a separate repository, included here as a git submodule:

- **`companion/forge-ai-api/`** — source of the forge-ai-api service (FastAPI → ASP.NET Core, Qdrant, DeepInfra). Agents can read files here to understand the generation pipeline.
- **`companion/forge-ai-api-contract.md`** — quick-reference API contract (request/response shapes, env vars) without needing to read the full source.

To update the submodule to the latest forge-ai-api commit:
```bash
cd companion/forge-ai-api && git pull origin main
cd ../.. && git add companion/forge-ai-api && git commit -m "chore: bump forge-ai-api submodule"
```

## Build & Run Commands

```bash
# Build
dotnet build mtg-forge.sln

# Run locally (requires MongoDB, PostgreSQL, and forge-ai-api running)
cd mtg-forge.Api && dotnet run

# Local dev with Docker (starts MongoDB, PostgreSQL, Prometheus, Grafana)
docker compose -f docker-compose-local.yml up -d
```

Local dev: API on `http://localhost:5000`, MongoDB on port 27018. Swagger at `/swagger`.

## Architecture

```
Frontend (wwwroot/index.html) → DecksController → RagPipelineService → forge-ai-api (DeepInfra LLM)
                                       ↓                    ↓
                                  DeckService → MongoDB    PricingService → PostgreSQL
```

- **Single-page frontend** (`wwwroot/index.html`): ~2000-line vanilla JS/HTML/CSS file with MTG-themed UI. Uses Scryfall API for card images. No build tooling or framework.
- **DecksController**: REST API for deck CRUD, AI generation, AI analysis, CSV import/export (supports moxfield, archidekt, deckbox, deckstats formats).
- **RagPipelineService** (sole `IDeckGenerationService`): Proxies deck generation to forge-ai-api (`POST /api/decks/generate`). Also calls DeepInfra directly (OpenAI-compatible API) for deck analysis and import descriptions.
- **ClaudeService**: Legacy service; calls Anthropic Messages API. Not registered or used in the current production configuration.
- **DeckService**: MongoDB CRUD with `MongoDB.Driver`. Collection: `decks` in `mtgdeckforge` database.
- **Models** (`Models/`): `DeckConfiguration` (main document), `CardEntry`, `DeckGenerationRequest`, `DeckAnalysis`, settings POCOs.

## Configuration

Key environment variables:
- `RagPipeline__BaseUrl` — URL of forge-ai-api (e.g. `http://mtg-forge-ai.railway.internal:8080`)
- `RagPipeline__LlmBaseUrl` — OpenAI-compatible LLM base URL for analysis calls (DeepInfra in production: `https://api.deepinfra.com/v1/openai`)
- `RagPipeline__LlmApiKey` — API key for the LLM provider
- `RagPipeline__Model` — model name (e.g. `meta-llama/Llama-3.3-70B-Instruct`)
- `MongoDb__ConnectionString`, `MongoDb__DatabaseName`
- `DATABASE_URL` or `SqlStorage__ConnectionString` — PostgreSQL connection string
- `JWT_SECRET`, `ADMIN_PASSWORD`
- `ANTHROPIC_API_KEY` — only needed if manually switching to `ClaudeService` (not used in production)

## Key Endpoints

- `POST /api/decks/generate` — AI deck generation (async, returns job ID; poll `/api/decks/jobs/{jobId}`)
- `POST /api/decks/{id}/analyze` — AI deck analysis
- `GET /api/decks/{id}/export/csv?format=moxfield` — export (moxfield/archidekt/deckbox/deckstats)
- `POST /api/decks/import/csv` — import with auto-format detection
- `GET /healthz` — health check

## Notes

- Tests live in `mtg-forge.Tests` (xUnit). Run with `dotnet test mtg-forge.sln`.
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
