# ⚔️ MTG Deck Forge

A Magic: The Gathering deck configuration generator powered by AI, built with .NET 8, MongoDB, PostgreSQL, and Docker.

![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)
![MongoDB](https://img.shields.io/badge/MongoDB-7-green)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-blue)
![Docker](https://img.shields.io/badge/Docker-Compose-blue)

## Features

- **AI-Powered Deck Generation** — Two swappable providers: Anthropic Claude API (`LlmProvider=Claude`) or a local RAG pipeline via `mtg-forge-local` + Qdrant + Ollama (`LlmProvider=Rag`, default)
- **Budget-Aware Generation** — Real prices from MTGJSON/TCGPlayer are applied after generation; over-budget decks are automatically fixed with cheaper alternatives
- **Rich Configuration Options** — Choose mana colors, format (Commander/Standard/Modern/Pioneer/Legacy/Pauper/Vintage), power level, budget range, strategy archetype, and more
- **Persistent Storage** — Decks stored in MongoDB; authentication and price cache stored in PostgreSQL
- **Razor Pages UI** — Server-rendered login and deck workflows with ASP.NET Core Identity cookie auth
- **JWT + Cookie Auth** — Dual authentication: JWT Bearer for API clients, cookie auth for Razor Pages
- **Daily MTGJSON Pricing Import** — Pulls MTGJSON bulk pricing daily and caches it in PostgreSQL
- **Full CRUD** — Create (generate), Read (list/detail/search), Update (patch), Copy, Delete deck configurations
- **CSV Import/Export** — Supports moxfield, archidekt, deckbox, deckstats formats (auto-detected on import)
- **Deck Analysis** — AI-powered analysis with synergy assessment, weaknesses, and upgrade suggestions
- **Price Lookup** — Card price search via Scryfall + local MTGJSON/TCGPlayer cache
- **Observability** — Prometheus metrics, Grafana dashboards, OpenTelemetry traces, Serilog structured logging
- **Rate Limiting** — 20 deck generations per user per 24 hours
- **Swagger API Docs** — Available at `/swagger` for testing endpoints directly

## Architecture

```
Browser (wwwroot/index.html — vanilla JS SPA)
    │
    ▼
.NET 8 ASP.NET Core API  (Razor Pages + REST Controllers)
    ├──► IDeckGenerationService  ──► ClaudeService        (LlmProvider=Claude — Anthropic API)
    │                             └► RagPipelineService   (LlmProvider=Rag — mtg-forge-local + Qdrant + Ollama)
    ├──► DeckService      → MongoDB (decks collection)
    ├──► PricingService   → PostgreSQL (MTGJSON/TCGPlayer price cache)
    ├──► ScryfallService  → Scryfall API (card enrichment, image lookup)
    └──► AuthService      → PostgreSQL (ASP.NET Identity)

Monitoring stack (docker-compose.yml):
    ├── Prometheus  → scrapes /metrics
    └── Grafana     → dashboards from Prometheus data
```

**LLM Provider toggle** — set `"LlmProvider"` in `appsettings.json` or as an environment variable:
- `"Rag"` (default) → routes to `mtg-forge-local` at `RagPipeline__BaseUrl` via `RagPipelineService`
- `"Claude"` → calls Anthropic API via `ClaudeService`

See [LOCAL-LLM-SETUP.md](LOCAL-LLM-SETUP.md) and [RAILWAY-RAG-SETUP.md](RAILWAY-RAG-SETUP.md) for RAG pipeline setup.

## Quick Start

### Prerequisites

- Docker & Docker Compose
- Either an [Anthropic API key](https://console.anthropic.com/) (for `LlmProvider=Claude`) or `mtg-forge-local` running (for `LlmProvider=Rag`)

### 1. Clone & Configure

```bash
git clone <your-repo-url>
cd MtgDeckForge

# Create your environment file
cp .env.example .env

# Edit .env — set ANTHROPIC_API_KEY (if using Claude), ADMIN_PASSWORD, JWT_SECRET
nano .env
```

### 2. Launch

```bash
docker compose up -d --build
```

### 3. Open

Navigate to **http://localhost:5000** in your browser.

- **Forge tab** — Configure and generate new decks
- **Library tab** — Browse, view, and manage saved decks
- **Swagger** — http://localhost:5000/swagger for API exploration

## API Endpoints

### Decks

| Method     | Endpoint                         | Description                                      |
|------------|----------------------------------|--------------------------------------------------|
| `GET`      | `/api/decks`                     | List decks (paginated; `?name=&color=&format=&powerLevel=&skip=&limit=`) |
| `GET`      | `/api/decks/{id}`                | Get a specific deck by ID                        |
| `GET`      | `/api/decks/search`              | Search decks by `?color=B&format=Commander`      |
| `POST`     | `/api/decks/generate`            | Generate a new deck via AI (rate-limited: 20/24h)|
| `PATCH`    | `/api/decks/{id}`                | Update deck name, cards, or metadata             |
| `POST`     | `/api/decks/{id}/copy`           | Duplicate a deck                                 |
| `POST`     | `/api/decks/{id}/analyze`        | AI-powered deck analysis                         |
| `DELETE`   | `/api/decks/{id}`                | Delete a deck                                    |
| `GET`      | `/api/decks/{id}/export/csv`     | Export to CSV (`?format=moxfield\|archidekt\|deckbox\|deckstats`) |
| `POST`     | `/api/decks/import/csv`          | Import from CSV (auto-detects format)            |

### Auth

| Method   | Endpoint              | Description                          |
|----------|-----------------------|--------------------------------------|
| `POST`   | `/api/auth/login`     | Login — returns JWT token            |
| `POST`   | `/api/auth/register`  | Register a new user (Admin only)     |
| `GET`    | `/api/auth/me`        | Get current user profile             |

### Pricing

| Method   | Endpoint                      | Description                                      |
|----------|-------------------------------|--------------------------------------------------|
| `GET`    | `/api/pricing/search`         | Search cards by name (`?q=&page=`) via Scryfall  |
| `GET`    | `/api/pricing/lookup`         | Full price detail from local DB + Scryfall (`?cardName=`) |
| `POST`   | `/api/pricing/refresh`        | Trigger manual MTGJSON pricing import (Admin)    |
| `GET`    | `/api/pricing/import-runs`    | List recent pricing import runs (Admin)          |

### Groups (Admin only)

| Method   | Endpoint              | Description                    |
|----------|-----------------------|--------------------------------|
| `GET`    | `/api/groups`         | List all groups                |
| `GET`    | `/api/groups/{id}`    | Get a group by ID              |
| `POST`   | `/api/groups`         | Create a group                 |
| `DELETE` | `/api/groups/{id}`    | Delete a group                 |

### System

| Method | Endpoint         | Description                                |
|--------|------------------|--------------------------------------------|
| `GET`  | `/healthz`       | Health check                               |
| `GET`  | `/api/version`   | Build version                              |
| `GET`  | `/metrics`       | Prometheus scrape endpoint (internal only) |
| `GET`  | `/logging`       | Recent structured logs (internal only)     |

### Example: Generate a Deck

```bash
curl -X POST http://localhost:5000/api/decks/generate \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <your-jwt-token>" \
  -d '{
    "colors": ["B", "G"],
    "format": "Commander",
    "powerLevel": "Casual",
    "budgetRange": "Budget",
    "preferredStrategy": "Aristocrats",
    "preferredCommander": "Meren of Clan Nel Toth"
  }'
```

## Configuration

### Environment Variables

| Variable                    | Description                                        | Default / Required         |
|-----------------------------|----------------------------------------------------|----------------------------|
| `LlmProvider`               | `Rag` or `Claude`                                  | `Rag`                      |
| `ANTHROPIC_API_KEY`         | Anthropic API key (required when `LlmProvider=Claude`) | *(required for Claude)* |
| `DATABASE_URL`              | PostgreSQL URI (Railway format, auto-converted)    | *(required in production)* |
| `JWT_SECRET`                | JWT signing key (min 32 chars)                     | *(required in production)* |
| `ADMIN_PASSWORD`            | Password for the seeded admin account              | *(required in production)* |
| `MongoDb__ConnectionString` | MongoDB connection string                          | `mongodb://mongodb:27017`  |
| `MongoDb__DatabaseName`     | MongoDB database name                              | `mtgdeckforge`             |
| `ClaudeApi__Model`          | Claude model to use                                | `claude-sonnet-4-20250514` |
| `ClaudeApi__MaxTokens`      | Max tokens for Claude generation                   | `16384`                    |
| `RagPipeline__BaseUrl`      | Base URL for mtg-forge-local (Rag provider)        | `http://mtg-forge-local.railway.internal:5000` |
| `RagPipeline__OllamaUrl`    | Ollama URL used for deck analysis (Rag provider)   | `http://ollama.railway.internal:11434` |
| `RagPipeline__Model`        | Ollama generation model name                       | `mistral`                  |
| `CORS_ALLOWED_ORIGINS`      | Comma-separated allowed origins (production)       | *(open in development)*    |

## Observability

- **Health Check** — `/healthz` (returns `{"status":"healthy"}`)
- **Prometheus Metrics** — `/metrics` (internal only; scraped by Prometheus container)
- **Recent Logs** — `/logging?count=200` (internal only; Serilog structured events)
- **Grafana** — `http://localhost:3000` (dashboards provisioned from `monitoring/grafana/`)
- **Application Logs** — `docker compose logs -f mtg-api`
- **MongoDB** — Accessible on `localhost:27017` for MongoDB Compass (local dev on `27018`)

## Development (Without Docker)

```bash
# 1. Start infrastructure with Docker
docker compose -f docker-compose-local.yml up -d
# MongoDB :27018, PostgreSQL :5433, Prometheus :9090, Grafana :3000

# 2. Run the API
cd MtgDeckForge.Api
# For Claude provider:
export ANTHROPIC_API_KEY=sk-ant-xxxxx
# For Rag provider (default): ensure mtg-forge-local is running at localhost:5000
dotnet run
```

Default login page is at `/Account/Login`. API at `http://localhost:5001`, Swagger at `http://localhost:5001/swagger`.

### Pricing refresh

- Automatic refresh runs daily via a hosted background service.
- Admin can trigger a manual refresh:

```bash
curl -X POST http://localhost:5001/api/pricing/refresh \
  -H "Authorization: Bearer <admin-jwt-token>"
```

## Project Structure

```
MtgDeckForge/
├── docker-compose.yml           # Production stack: API + MongoDB + Prometheus + Grafana
├── docker-compose-local.yml     # Local dev dependencies: MongoDB + PostgreSQL + monitoring
├── Dockerfile                   # Multi-stage .NET 8 build
├── MtgDeckForge.sln             # Solution file
├── LOCAL-LLM-SETUP.md           # Local Rag pipeline setup guide
├── RAILWAY-RAG-SETUP.md         # Railway deployment guide (Rag provider)
├── monitoring/
│   ├── prometheus/              # Prometheus config (prometheus.yml, prometheus-local.yml)
│   └── grafana/provisioning/    # Grafana datasource + dashboard provisioning
├── MtgDeckForge.Api/
│   ├── MtgDeckForge.Api.csproj
│   ├── Program.cs               # Service registration, auth, middleware, rate limiting
│   ├── appsettings.json         # Default configuration
│   ├── Controllers/
│   │   ├── DecksController.cs   # Deck CRUD, generate, analyze, CSV import/export
│   │   ├── AuthController.cs    # Login, register, /me
│   │   ├── PricingController.cs # Price search, lookup, import refresh
│   │   └── GroupsController.cs  # Admin group management
│   ├── Data/
│   │   └── AppDbContext.cs      # EF Core: Identity + CardPrices + PricingImportRuns
│   ├── Migrations/              # EF Core PostgreSQL migrations
│   ├── Models/
│   │   ├── DeckModels.cs        # DeckConfiguration, CardEntry, DeckGenerationRequest, DeckAnalysis
│   │   ├── UserModels.cs        # User, Group, auth request/response models
│   │   ├── ApplicationUser.cs   # ASP.NET Identity user
│   │   ├── CardPrice.cs         # MTGJSON price entity
│   │   ├── ClaudeApiSettings.cs
│   │   ├── RagPipelineSettings.cs
│   │   ├── MongoDbSettings.cs
│   │   ├── JwtSettings.cs
│   │   ├── MtgJsonSettings.cs
│   │   └── SqlStorageSettings.cs
│   ├── Observability/
│   │   ├── InMemoryLogStore.cs   # In-memory Serilog sink for /logging endpoint
│   │   └── InternalOnlyMiddleware.cs
│   ├── Pages/                   # Razor Pages (Account/Login, Decks, etc.)
│   ├── Services/
│   │   ├── IDeckGenerationService.cs    # LLM provider abstraction
│   │   ├── ClaudeService.cs             # Anthropic API provider
│   │   ├── RagPipelineService.cs        # mtg-forge-local RAG provider
│   │   ├── DeckService.cs               # MongoDB CRUD
│   │   ├── ScryfallService.cs           # Scryfall enrichment
│   │   ├── PricingService.cs            # Price application from local DB
│   │   ├── MtgJsonPricingImportService.cs # MTGJSON bulk import
│   │   ├── PricingRefreshHostedService.cs # Daily pricing refresh background service
│   │   ├── AuthService.cs               # JWT generation, password hashing
│   │   └── UserService.cs               # MongoDB user/group CRUD
│   └── wwwroot/
│       └── index.html           # MTG-themed vanilla JS SPA frontend
└── MtgDeckForge.Tests/
    ├── ClaudeServiceTests.cs
    ├── DecksControllerCsvHelpersTests.cs
    └── ScryfallServiceTests.cs
```

## License

MIT
