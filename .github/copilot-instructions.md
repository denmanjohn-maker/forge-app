# MtgDeckForge — Copilot Instructions

## Architecture

```
Browser (wwwroot/index.html — vanilla JS SPA, ~2000 lines)
    │
    ▼
.NET 8 ASP.NET Core API  (Razor Pages + REST Controllers)
    ├──► IDeckGenerationService  ──► ClaudeService     (Anthropic API)
    │                             ├──► OllamaService   (hosted Ollama, default in appsettings)
    │                             └──► LocalLlmService (mtg-forge-local + Ollama, local dev)
    ├──► DeckService         → MongoDB (decks collection)
    ├──► PricingService      → PostgreSQL (MTGJSON price data)
    ├──► ScryfallService     → Scryfall API (card enrichment, image lookup)
    ├──► UserService/AuthService → MongoDB + PostgreSQL (ASP.NET Identity)
    └──► PricingRefreshHostedService (background MTGJSON sync, IHostedService)
```

**LLM Provider toggle** — set `"LlmProvider"` in `appsettings.json` (or env var):
| Value | Service | Notes |
|-------|---------|-------|
| `"Ollama"` | `OllamaService` | Default in appsettings.json; targets `ollama.railway.internal:11434` |
| `"Claude"` | `ClaudeService` | Anthropic API; requires `ANTHROPIC_API_KEY` |
| `"Local"` | `LocalLlmService` | `mtg-forge-local` sidecar at `localhost:5001` + Qdrant price pre-filter |

## Build & Run

```bash
# Build
dotnet build MtgDeckForge.sln

# Run (requires MongoDB + PostgreSQL)
cd MtgDeckForge.Api && dotnet run

# Local dev with Docker (MongoDB + PostgreSQL + Ollama included)
docker compose -f docker-compose-local.yml up -d --build

# Tests
dotnet test MtgDeckForge.sln

# Single test class
dotnet test MtgDeckForge.Tests --filter "FullyQualifiedName~ClaudeServiceTests"

# Single test method
dotnet test MtgDeckForge.Tests --filter "FullyQualifiedName~ClaudeServiceTests.GenerateDeckAsync_ParsesJsonFromMarkdownCodeBlock"
```

Local dev: API on `http://localhost:5000`, Swagger at `/swagger`. The listen port defaults to `5000` but honours the `PORT` env var (injected by Railway at runtime).

EF Core migrations (PostgreSQL schema):
```bash
cd MtgDeckForge.Api
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

## Key Conventions

### LLM Abstraction
`IDeckGenerationService` (`Services/IDeckGenerationService.cs`) is the single seam for swapping LLM providers:
- `GenerateDeckAsync(DeckGenerationRequest)` → `DeckConfiguration`
- `AnalyzeDeckAsync(DeckConfiguration)` → `DeckAnalysis`
- `SuggestBudgetReplacementsAsync(...)` → `List<CardEntry>` (returns `[]` in `LocalLlmService` — budget is pre-filtered upstream)
- `GenerateImportDescriptionAsync(...)` → `string`

`ClaudeService`, `OllamaService`, and `LocalLlmService` all implement this interface. Registration is in `Program.cs` based on `"LlmProvider"` config.

### Budget Enforcement
`ClaudeService.GetBudgetMax(string budgetRange)` is a static helper. Budget tier strings: `"Budget"` ($50), `"$50-$150"`, `"$150-$500"`, anything else = no limit.

The enforcement loop in `DecksController.Generate`:
1. Generate deck via `IDeckGenerationService.GenerateDeckAsync`
2. Apply real prices via `PricingService.ApplyPricesAsync` (from MTGJSON PostgreSQL data)
3. If over budget: call `SuggestBudgetReplacementsAsync` (Claude/Ollama retry, Local skips gracefully)

### Data Storage — Two Databases
- **MongoDB** (`DeckService`): deck documents (`DeckConfiguration` with embedded `List<CardEntry>`); also users/groups collections
- **PostgreSQL** (`AppDbContext`): ASP.NET Identity users + MTGJSON pricing data (`CardPrices`, `PricingImportRuns`)

`DeckService` is Singleton (MongoDB driver is thread-safe). `PricingService` is Scoped. `AuthService`/`UserService` are Singleton.

### Models
- `DeckConfiguration` — MongoDB document; persisted deck including cards, analysis, metadata
- `CardEntry` — embedded sub-document inside `DeckConfiguration.Cards`
- `DeckGenerationRequest` — API input; `Format` is a string (`"Commander"`, `"Standard"`, etc.), `PowerLevel` is a string (`"Casual"`, `"Focused"`, `"Optimized"`, `"Competitive"`)
- `DeckAnalysis` — persisted as `DeckConfiguration.LastAnalysis` after `/analyze`

### CSV Export/Import
`DecksController` supports 4 formats: `moxfield`, `archidekt`, `deckbox`, `deckstats`. Auto-detected on import from header fields. After import, Scryfall enriches `ManaCost`/`Cmc`/`CardType`, and `PricingService` applies real prices.

### Auth
Dual-auth: JWT Bearer for API clients, ASP.NET Identity cookie for Razor Pages (`/Account/Login`). The `"smart"` policy scheme in `Program.cs` routes between them based on the `Authorization` header prefix. `GroupsController` is admin-only (`[Authorize(Roles = "Admin")]`).

### Rate Limiting
`"deck-generation"` rate limiter: 20 requests per user per 24 hours (keyed by `ClaimTypes.NameIdentifier`, anonymous bucketed together). Applied on `POST /api/decks/generate` via `[EnableRateLimiting("deck-generation")]`.

### Configuration
All secrets via environment variables — `appsettings.json` has safe defaults for local dev only:
- `ANTHROPIC_API_KEY` — Claude API key (only needed when `LlmProvider=Claude`)
- `DATABASE_URL` — PostgreSQL URI (Railway format; converted to Npgsql key-value in `Program.cs`)
- `JWT_SECRET`, `ADMIN_PASSWORD`

Production uses Railway env injection.

### Observability
- **Serilog**: structured console logging + in-memory log store (last 1000 entries); accessible at `GET /logging` (internal only)
- **OpenTelemetry**: metrics exported via Prometheus scrape endpoint at `GET /metrics` (internal only)
- `/metrics` and `/logging` are blocked by `InternalOnlyMiddleware` — only reachable from Docker-internal IPs
- Monitoring stack in `monitoring/` (Prometheus + Grafana)

### Frontend
Single file `wwwroot/index.html` — vanilla JS/HTML/CSS, no build step. Uses Scryfall image API for card art. Three Google Fonts: Cinzel, Crimson Text, MedievalSharp. Requires `-webkit-backdrop-filter` prefix and `dvh` fallback for iOS Safari compatibility.

### Razor Pages
Under `Pages/Account` (login/register) and `Pages/Decks`. These use Identity cookie auth, not JWT. `UseForwardedHeaders` is configured for Railway reverse-proxy compatibility (required for iOS Safari).

### Tests
- Framework: xUnit. No mocking library — tests use a hand-rolled `StubHttpMessageHandler` to fake `HttpClient` responses.
- Private/internal controller helpers (e.g., CSV parsing) are tested via reflection (`MethodInfo` + `Invoke`).
- Known pre-existing failure: `ClaudeServiceTests.GenerateDeckAsync_ParsesJsonFromMarkdownCodeBlock` — `Assert.Single` fails because Commander deck generation pads the card list with basic lands.

### Deployment
Staging-first workflow: develop on `staging` branch, promote to production with:
```bash
git push origin staging:main --force
```
Hosted on Railway. Staging URL: `staging.bensmagicforge.app`.
