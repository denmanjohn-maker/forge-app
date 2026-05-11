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
- Deck generation uses `IDeckGenerationService`, an abstraction with two implementations: `ClaudeService` (Anthropic API) and `RagPipelineService` (mtg-forge-ai + Ollama). The provider is selected via the `LlmProvider` config key (`Claude` or `Rag`). `DecksController.Generate` calls the active implementation, then `PricingService.ApplyPricesAsync`, then persists the finished `DeckConfiguration` through `DeckService`.
- CSV import is a multi-step pipeline in `DecksController.ImportCsv`: parse CSV with private static helpers, enrich cards through `ScryfallService`, overlay local prices from `PricingService`, derive deck colors, generate an import description with `IDeckGenerationService`, then save to MongoDB.
- Pricing data is refreshed in the background. `PricingRefreshHostedService` runs daily and calls `MtgJsonPricingImportService`, which streams large MTGJSON payloads into PostgreSQL instead of loading them fully into memory.
- Authentication is split by client type. `Program.cs` configures a `"smart"` auth policy that uses JWT bearer tokens for API clients with an `Authorization: Bearer ...` header and falls back to the ASP.NET Identity cookie flow for Razor Pages.
- Observability is built in. Serilog writes to console and an in-memory log store, OpenTelemetry exposes Prometheus metrics, and `/metrics` plus `/logging` are protected by `InternalOnlyMiddleware`.

## Key conventions

- Most API controllers are `[Authorize]` by default. `GroupsController` and several auth-management endpoints are admin-only, and deck ownership checks are enforced in controllers by comparing `DeckConfiguration.UserId` to the current claim unless the caller is in the `Admin` role.
- `DeckService` and `UserService` are Mongo-backed singleton services that create indexes themselves. `PricingService` is scoped because it depends on `AppDbContext`.
- Price lookups depend on normalized card names. Reuse `PricingService.NormalizeCardName` instead of introducing alternate normalization logic.
- `ScryfallService.EnrichCardsAsync` is intentionally non-destructive: it fills missing mana cost, CMC, type, and price fields, but does not overwrite populated values. It batches requests in groups of 75 to match Scryfall's collection API limits.
- The SPA is a single large `wwwroot/index.html` file with no frontend build step. If you touch the CSS or layout, preserve the existing iOS/Safari compatibility details such as `min-height: 100dvh` and `-webkit-backdrop-filter`.
- Tests are xUnit-only and avoid mocking libraries. HTTP-dependent services are tested with hand-rolled `HttpMessageHandler` stubs, and private static CSV helper methods in `DecksController` are tested via reflection rather than being made public just for tests.

## Local environment details

- Host-based local development uses `docker-compose-local.yml` for dependencies only: MongoDB on `localhost:27018`, PostgreSQL on `localhost:5433`, Prometheus on `localhost:9090`, and Grafana on `localhost:3000`.
- The app honors the `PORT` environment variable and otherwise listens on `5000`.
- Key runtime configuration comes from environment variables or `appsettings.json`, especially `ANTHROPIC_API_KEY`, `DATABASE_URL` or `SqlStorage:ConnectionString`, `JWT_SECRET`, and `ADMIN_PASSWORD`.

## Companion repositories

- **forge-ai-api** (`../forge-ai-api`) — the local RAG pipeline this app calls when `LlmProvider = Rag`. It runs MongoDB + Qdrant + Ollama and exposes `/api/decks/generate`. The `RagPipelineService` in this repo proxies to it.
- **forge-observability** (`../forge-observability`) — a standalone Grafana / Prometheus / Loki / Tempo / Alloy stack. The `docker-compose-local.yml` here includes Prometheus and Grafana for convenience; the observability repo is used for full-stack or Railway deployments.
