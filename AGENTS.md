<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **forge-app** (3385 symbols, 6708 relationships, 168 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

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

# mtg-forge — Agent Instructions

## Commands
```bash
# Build
dotnet build mtg-forge.sln

# Tests
dotnet test mtg-forge.sln
dotnet test mtg-forge.Tests --filter "FullyQualifiedName~YourTestClass"

# Local dependencies (MongoDB, PostgreSQL, Prometheus, Grafana)
docker compose -f docker-compose-local.yml up -d

# Run API
cd mtg-forge.Api && dotnet run

# DB Migrations (PostgreSQL)
cd mtg-forge.Api && dotnet ef database update
```

## Architecture & Boundaries
- **Project Structure**: `.NET 10` Web API hosting REST controllers, Razor Pages, and a **Vanilla JS SPA** (`wwwroot/index.html`). No frontend build step or framework exists.
- **AI Backend**: **`ClaudeService` is NOT used in production.** Deck generation delegates entirely to `forge-ai-api` (RAG pipeline) via `RagPipelineService`.
- **Companion Repo**: The `forge-ai-api` service is a git submodule at `companion/forge-ai-api/`. Check `companion/forge-ai-api-contract.md` for fast API reference.
- **Dual Databases**: 
  - `MongoDB` (`mtgdeckforge`): Deck documents, Users, Groups.
  - `PostgreSQL`: ASP.NET Identity tables, `CardPrices`, `PricingImportRuns`.
- **Authentication**: A "smart" auth policy resolves JWT bearer tokens for API clients and ASP.NET Identity cookies for Razor Pages. Do not strictly rely on ASP.NET Identity for API login.

## Quirks & Conventions
- **Scryfall HTTP Client**: Requests to Scryfall **MUST** include `User-Agent: mtg-forge/1.0` and `Accept: application/json`. Both are configured centrally in `Program.cs` via `AddHttpClient` — do not set them manually in service constructors.
- **Proxy PDFs (QuestPDF)**: `ProxyService` generates 3x3 proxy grids. Scryfall images **must** use `.FitUnproportionally()` to fill the slot, avoiding default aspect ratio constraints that break the layout.
- **Async Deck Generation**: `DecksController.Generate` is **fire-and-forget**. It creates a `GenerationJob`, runs processing in a background task, and immediately returns 202. The client polls for status.
- **Budget Loop**: After deck generation, an automated loop compares deck cost using real `MTGJSON` prices from the PostgreSQL cache, replacing cards that exceed the budget ceiling.
- **Pricing Data Import**: Large MTGJSON dumps stream incrementally (`MtgJsonPricingImportService`) rather than buffering into memory to prevent OOM errors.
- **Testing**: Only xUnit is used. Avoid mocking libraries; use hand-rolled stubs (`HttpMessageHandler`). Test private methods via reflection if needed rather than making them public.