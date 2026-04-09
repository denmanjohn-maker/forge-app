# MtgDeckForge — Copilot Instructions

## Architecture

```
Browser (wwwroot/index.html — vanilla JS SPA, ~2000 lines)
    │
    ▼
.NET 8 ASP.NET Core API  (Razor Pages + REST Controllers)
    ├──► IDeckGenerationService  ──► ClaudeService        (Anthropic API)
    │                             └► RagPipelineService   (mtg-forge-local + Qdrant + Ollama, default)
    ├──► DeckService      → MongoDB (decks collection)
    ├──► PricingService   → PostgreSQL (MTGJSON price data)
    ├──► ScryfallService  → Scryfall API (card enrichment, image lookup)
    └──► AuthService      → PostgreSQL (ASP.NET Identity)
```

**LLM Provider toggle** — set `"LlmProvider"` in `appsettings.json` (or env var):
- `"Rag"` (default) → calls `mtg-forge-local` at `RagPipeline__BaseUrl` via `RagPipelineService`
- `"Claude"` → calls Anthropic API via `ClaudeService`

## Build & Run

```bash
# Build
dotnet build MtgDeckForge.sln

# Run (requires MongoDB + PostgreSQL)
cd MtgDeckForge.Api && dotnet run

# Local dev with Docker (MongoDB + PostgreSQL included)
docker compose -f docker-compose-local.yml up -d

# Tests
dotnet test MtgDeckForge.sln

# Single test
dotnet test MtgDeckForge.Tests --filter "FullyQualifiedName~ClaudeServiceTests"
```

Local dev: API on `http://localhost:5001`, Swagger at `/swagger`.

## Switching to Rag Provider

Requires `mtg-forge-local` running at `localhost:5000`. See LOCAL-LLM-SETUP.md for full instructions.

```bash
# 1. Start infrastructure (mtg-forge-local)
cd "/Users/johndenman/Desktop/Local LLM Magic/mtg-forge-local"
docker compose up -d          # MongoDB + Qdrant
cd scripts && python ingest_cards.py  # One-time card ingestion (~15 min)
cd MtgForgeLocal && dotnet run        # mtg-forge-local API on :5000

# 2. Toggle in MtgDeckForge appsettings.json (already default)
"LlmProvider": "Rag"

# 3. Run MtgDeckForge normally
cd MtgDeckForge.Api && dotnet run
```

**Why this solves budget issues:** `RagPipelineService` routes deck generation to `mtg-forge-local`, which pre-filters cards by `price_usd` in Qdrant *before* they reach the LLM — so the model only sees affordable cards and can't hallucinate prices.

## Key Conventions

### LLM Abstraction
`IDeckGenerationService` (`Services/IDeckGenerationService.cs`) is the single seam for swapping LLM providers:
- `GenerateDeckAsync(DeckGenerationRequest)` → `DeckConfiguration`
- `AnalyzeDeckAsync(DeckConfiguration)` → `DeckAnalysis`
- `SuggestBudgetReplacementsAsync(...)` → `List<CardEntry>` (returns `[]` in `RagPipelineService` — budget is pre-filtered)
- `GenerateImportDescriptionAsync(...)` → `string`

Both `ClaudeService` and `RagPipelineService` implement this interface. Registration is in `Program.cs` based on `"LlmProvider"` config.

### Budget Enforcement
`ClaudeService.GetBudgetMax(string budgetRange)` is a static helper — call it even when using `RagPipelineService`. Budget tier strings: `"Budget"` ($50), `"$50-$150"`, `"$150-$500"`, anything else = no limit.

The enforcement loop in `DecksController.Generate`:
1. Generate deck via `IDeckGenerationService.GenerateDeckAsync`
2. Apply real prices via `PricingService.ApplyPricesAsync` (from MTGJSON PostgreSQL data)
3. If over budget: call `SuggestBudgetReplacementsAsync` (Claude retries, Rag skips gracefully)

### Data Storage — Two Databases
- **MongoDB** (`DeckService`): deck documents (`DeckConfiguration` with embedded `List<CardEntry>`)
- **PostgreSQL** (`AppDbContext`): ASP.NET Identity users + MTGJSON pricing data

`DeckService` is Singleton (MongoDB driver is thread-safe). `PricingService` is Scoped.

### Models
- `DeckConfiguration` — MongoDB document; persisted deck including cards, analysis, metadata
- `CardEntry` — embedded sub-document inside `DeckConfiguration.Cards`
- `DeckGenerationRequest` — API input; `Format` is a string (`"Commander"`, `"Standard"`, etc.), `PowerLevel` is a string (`"Casual"`, `"Focused"`, `"Optimized"`, `"Competitive"`)
- `DeckAnalysis` — persisted as `DeckConfiguration.LastAnalysis` after `/analyze`

### CSV Export/Import
`DecksController` supports 4 formats: `moxfield`, `archidekt`, `deckbox`, `deckstats`. Auto-detected on import from header fields. After import, Scryfall enriches `ManaCost`/`Cmc`/`CardType`, and `PricingService` applies real prices.

### Auth
Dual-auth: JWT Bearer for API clients, ASP.NET Identity cookie for Razor Pages (`/Account/Login`). The `"smart"` policy scheme routes between them based on the `Authorization` header prefix.

### Configuration
All secrets via environment variables — `appsettings.json` has safe defaults for local dev only:
- `ANTHROPIC_API_KEY` — Claude API key (not needed when `LlmProvider=Rag`)
- `DATABASE_URL` — PostgreSQL URI (Railway format; converted to Npgsql in `Program.cs`)
- `JWT_SECRET`, `ADMIN_PASSWORD`

Production uses Railway environment variable injection.

### Frontend
Single file `wwwroot/index.html` — vanilla JS/HTML/CSS, no build step. Uses Scryfall image API for card art. Three Google Fonts: Cinzel, Crimson Text, MedievalSharp.
