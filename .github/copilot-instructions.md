# Copilot Instructions for mtg-forge

## Build and test commands

```bash
# Restore
dotnet restore mtg-forge.sln

# Build the whole solution
dotnet build mtg-forge.sln

# Run the full test suite
dotnet test mtg-forge.sln

# Run a single test class
dotnet test mtg-forge.Tests --filter "FullyQualifiedName~ClaudeServiceTests"

# Run a single test method
dotnet test mtg-forge.Tests --filter "FullyQualifiedName~ClaudeServiceTests.GenerateDeckAsync_ParsesJsonFromMarkdownCodeBlock"

# Run the API locally from the project directory
cd mtg-forge.Api && dotnet run

# Start local dependencies for host-based development
docker compose -f docker-compose-local.yml up -d

# Apply PostgreSQL migrations when schema changes
cd mtg-forge.Api && dotnet ef database update
```

There is no dedicated lint command or lint configuration checked into this repository.

The repository's main automated verification lives in `.github/workflows/post-merge-review.yml`, which restores, builds in Release, runs `dotnet test`, and checks vulnerable NuGet packages with `dotnet list ... --vulnerable`.

## High-level architecture

- `mtg-forge.Api` is a single ASP.NET Core web app that serves three surfaces from one process: REST controllers, Razor Pages under `Pages/`, and the static SPA in `wwwroot/index.html`.
- The app uses **two databases**. MongoDB stores deck documents plus the app's own `User` and `Group` records. PostgreSQL stores ASP.NET Identity tables and the MTGJSON pricing cache in `AppDbContext`.
- Deck generation is handled exclusively by `RagPipelineService` (the sole `IDeckGenerationService` registration). It proxies deck generation to **forge-ai-api** (`RagPipeline:BaseUrl`), which runs Qdrant vector search + a hosted LLM. Post-generation, `RagPipelineService` calls Together.ai (OpenAI-compatible `/v1/chat/completions`) directly for deck analysis, budget replacement suggestions, and CSV import descriptions — configured via `RagPipeline:LlmBaseUrl` / `RagPipeline:LlmApiKey`.
- `DecksController.Generate` is **fire-and-forget**: it immediately creates a `GenerationJob` via `GenerationJobStore`, starts the work in a background `Task.Run`, and returns the job ID (202). The SPA polls `GET /api/decks/jobs/{jobId}` until the status is `Completed` or `Failed`. The job store is singleton, backed by a `ConcurrentDictionary`, and auto-purges jobs older than 1 hour.
- After `RagPipelineService` returns a deck, the generate pipeline applies a **budget enforcement loop** (up to 3 retries): it identifies cards over the per-card price ceiling and replaces them with cheap cards fetched from `PricingService.GetCheapCardsAsync`.
- CSV import is a multi-step pipeline in `DecksController.ImportCsv`: parse CSV with private static helpers, enrich cards through `ScryfallService`, overlay local prices from `PricingService`, derive deck colors, generate an import description with `IDeckGenerationService`, then save to MongoDB.
- Pricing data is refreshed in the background. `PricingRefreshHostedService` runs daily and calls `MtgJsonPricingImportService`, which streams large MTGJSON payloads into PostgreSQL instead of loading them fully into memory.
- `DeckReanalysisHostedService` runs daily (5-minute startup delay) and re-analyzes decks whose `AnalysisUpdatedAt` is more than 7 days stale (up to 20 per run).
- `AiUsageService` records every LLM call (user, operation, token counts) into a MongoDB `aiUsage` collection. This data is exposed via admin endpoints.
- Authentication is split by client type. `Program.cs` configures a `"smart"` auth policy that uses JWT bearer tokens for API clients with an `Authorization: Bearer ...` header and falls back to the ASP.NET Identity cookie flow for Razor Pages.
- Observability is built in. Serilog writes to console, an in-memory log store, optional OTLP (via `OTEL_EXPORTER_OTLP_ENDPOINT`), and optional Loki (via `LOKI_URI`). OpenTelemetry exposes Prometheus metrics. `/metrics` and `/logging` are protected by `InternalOnlyMiddleware`. Custom OTel spans use `MtgForgeActivitySource` with semantic `gen_ai.*` attributes.
- See `ARCHITECTURE.md` for the full multi-service picture (forge-app, forge-ai-api, forge-observability) and Railway deployment topology.

## Key conventions

- Most API controllers are `[Authorize]` by default. `GroupsController` and several auth-management endpoints are admin-only. Deck ownership checks compare `DeckConfiguration.UserId` to the current claim; callers in the `Admin` role bypass the check.
- `DeckService`, `UserService`, `GenerationJobStore`, and `AiUsageService` are registered as **singletons**. `PricingService` is **scoped** because it depends on `AppDbContext`. Background tasks that need scoped services must call `_scopeFactory.CreateScope()` — see `DecksController.Generate` for the pattern.
- Price lookups depend on normalized card names. Reuse `PricingService.NormalizeCardName` instead of introducing alternate normalization logic.
- `ScryfallService.EnrichCardsAsync` is intentionally non-destructive: it fills missing mana cost, CMC, type, and price fields but does not overwrite populated values. It batches requests in groups of 75 to match Scryfall's collection API limits.
- `ThemedSetDetector.Detect` / `BuildPromptAddendum` augments `DeckGenerationRequest.AdditionalNotes` before it is sent to forge-ai-api. Call these methods when forwarding generation requests to keep Universes Beyond themed-set handling intact.
- The SPA is a single large `wwwroot/index.html` file with no frontend build step. If you touch the CSS or layout, preserve the existing iOS/Safari compatibility details such as `min-height: 100dvh` and `-webkit-backdrop-filter`.
- Tests are xUnit-only and avoid mocking libraries. HTTP-dependent services are tested with hand-rolled `HttpMessageHandler` stubs, and private static CSV helper methods in `DecksController` are tested via reflection rather than being made public just for tests.
- API login/JWT auth uses MongoDB `User` records via `UserService`/`AuthService`. ASP.NET Identity (`ApplicationUser`) is used only for the Razor Pages cookie flow and Identity table migrations — not for API registration or login.

## Local environment details

- Host-based local development uses `docker-compose-local.yml` for dependencies only: MongoDB on `localhost:27018`, PostgreSQL on `localhost:5433`, Prometheus on `localhost:9090`, and Grafana on `localhost:3000`.
- The app honors the `PORT` environment variable and otherwise listens on `5000`.
- Key runtime configuration comes from environment variables or `appsettings.json`, especially `ANTHROPIC_API_KEY`, `DATABASE_URL` or `SqlStorage:ConnectionString`, `JWT_SECRET`, and `ADMIN_PASSWORD`.

## Companion repositories

- **forge-ai-api** (`../forge-ai-api`) — the local RAG pipeline this app calls when `LlmProvider = Rag`. It runs MongoDB + Qdrant + Ollama and exposes `/api/decks/generate`. The `RagPipelineService` in this repo proxies to it.
- **forge-observability** (`../forge-observability`) — a standalone Grafana / Prometheus / Loki / Tempo / Alloy stack. The `docker-compose-local.yml` here includes Prometheus and Grafana for convenience; the observability repo is used for full-stack or Railway deployments.
