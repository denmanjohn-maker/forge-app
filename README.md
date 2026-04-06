# ⚔️ MTG Deck Forge

A Magic: The Gathering deck configuration generator powered by Claude AI, built with .NET 10, MongoDB, PostgreSQL, and Docker.

![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)
![MongoDB](https://img.shields.io/badge/MongoDB-7-green)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-blue)
![Docker](https://img.shields.io/badge/Docker-Compose-blue)

## Features

- **AI-Powered Deck Generation** — Uses the Anthropic Claude API to generate complete, format-legal deck configurations based on your parameters
- **Rich Configuration Options** — Choose mana colors, format (Commander/Standard/Modern/Pioneer), power level, budget range, strategy archetype, and more
- **CSV Import & Export** — Import decks from Moxfield, Archidekt, Deckbox, and Deckstats; export back to any of those formats
- **Card Pricing** — Daily MTGJSON bulk price import into PostgreSQL; per-card prices applied at generation and import time; live Scryfall price lookup
- **Deck Copy & Analysis** — Duplicate any deck or request an AI-powered analysis of its strengths and weaknesses
- **JWT + Cookie Auth** — All deck and pricing APIs require authentication. Login via the SPA (JWT) or the Razor Pages flow (cookie)
- **User & Group Management** — Admin users can create/delete accounts, reset passwords, and manage groups
- **Rate Limiting** — Deck generation is capped at 20 requests per user per 24 hours
- **Observability** — Serilog structured logging, Prometheus metrics via OpenTelemetry, Grafana Loki log push, and Grafana dashboards included out of the box
- **Scalar API Docs** — Interactive API reference at `/scalar/v1`; OpenAPI spec at `/openapi/v1.json`

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                      Docker Compose                          │
│                                                              │
│  ┌──────────────────┐  ┌─────────────┐  ┌───────────────┐  │
│  │  mtg-api          │  │  mongodb    │  │  postgres     │  │
│  │  .NET 10 Web API  │──│  Mongo 7    │  │  PG 16        │  │
│  │  + Static SPA     │  │  Port 27017 │  │  Port 5432    │  │
│  │  + Razor Pages    │  └─────────────┘  └───────────────┘  │
│  │  Port 5000        │                                       │
│  └────────┬──────────┘                                       │
│           │ HTTP to Claude API & Scryfall API                │
│           ▼                                                  │
│     api.anthropic.com / api.scryfall.com                     │
│                                                              │
│  ┌──────────────────┐  ┌─────────────────────────────────┐  │
│  │  prometheus      │  │  grafana                        │  │
│  │  Port 9090       │──│  Port 3000                      │  │
│  └──────────────────┘  └─────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

**Data stores:**
- **MongoDB** — deck configurations and user/group documents
- **PostgreSQL** — ASP.NET Core Identity (users/roles), card price cache (`CardPrices`), and pricing import run history (`PricingImportRuns`)

## Quick Start

### Prerequisites

- Docker & Docker Compose
- An [Anthropic API key](https://console.anthropic.com/)

### 1. Clone & Configure

```bash
git clone <your-repo-url>
cd MtgDeckForge

# Create your environment file
cp .env.example .env

# Edit .env — at minimum set ANTHROPIC_API_KEY, ADMIN_PASSWORD, and JWT_SECRET
nano .env
```

### 2. Launch

```bash
docker compose up -d --build
```

### 3. Open

Navigate to **http://localhost:5000** in your browser and log in with your admin credentials.

- **Forge tab** — Configure and generate new decks
- **Library tab** — Browse, view, and manage saved decks
- **Price Lookup** — Search cards and view live pricing from multiple sources
- **Scalar API docs** — http://localhost:5000/scalar/v1 for API exploration

## Authentication

All API endpoints (except `/healthz`, `/api/version`, and the OpenAPI/Scalar docs) require authentication.

**Login** via `POST /api/auth/login` to receive a JWT token, then pass it as `Authorization: Bearer <token>` on subsequent requests. The Razor Pages UI (`/Account/Login`) uses cookie auth instead.

```bash
# Get a JWT token
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "your-password"}'
```

## API Endpoints

### Decks

| Method    | Endpoint                         | Description                                          |
|-----------|----------------------------------|------------------------------------------------------|
| `GET`     | `/api/decks`                     | List decks (paginated; supports `?name=&color=&format=&powerLevel=&skip=&limit=`) |
| `GET`     | `/api/decks/{id}`                | Get a specific deck by ID                            |
| `GET`     | `/api/decks/search`              | Search decks by `?color=B&format=Commander`          |
| `POST`    | `/api/decks/generate`            | Generate a new deck via Claude AI (rate limited)     |
| `PATCH`   | `/api/decks/{id}`                | Update deck name/notes                               |
| `POST`    | `/api/decks/{id}/copy`           | Duplicate a deck                                     |
| `POST`    | `/api/decks/{id}/analyze`        | AI analysis of deck strengths/weaknesses             |
| `GET`     | `/api/decks/{id}/export/csv`     | Export deck as CSV (`?format=moxfield\|archidekt\|deckbox\|deckstats\|default`) |
| `POST`    | `/api/decks/import/csv`          | Import deck from CSV (auto-detects format)           |
| `DELETE`  | `/api/decks/{id}`                | Delete a deck                                        |

### Pricing

| Method   | Endpoint                        | Description                                           |
|----------|---------------------------------|-------------------------------------------------------|
| `GET`    | `/api/pricing/search`           | Card search via Scryfall (`?q=&page=`)                |
| `GET`    | `/api/pricing/lookup`           | Full price detail from local DB + Scryfall (`?cardName=`) |
| `POST`   | `/api/pricing/refresh`          | *(Admin)* Trigger a manual MTGJSON price import       |
| `GET`    | `/api/pricing/import-runs`      | *(Admin)* List recent pricing import run history      |

### Auth & Users

| Method   | Endpoint                              | Description                              |
|----------|---------------------------------------|------------------------------------------|
| `POST`   | `/api/auth/login`                     | Login and receive a JWT token            |
| `GET`    | `/api/auth/me`                        | Get current user info                    |
| `POST`   | `/api/auth/register`                  | *(Admin)* Create a new user              |
| `GET`    | `/api/auth/users`                     | *(Admin)* List all users                 |
| `POST`   | `/api/auth/users/{id}/reset-password` | *(Admin)* Reset a user's password        |
| `DELETE` | `/api/auth/users/{id}`                | *(Admin)* Delete a user                  |

### Groups *(Admin only)*

| Method   | Endpoint             | Description         |
|----------|----------------------|---------------------|
| `GET`    | `/api/groups`        | List all groups     |
| `GET`    | `/api/groups/{id}`   | Get group by ID     |
| `POST`   | `/api/groups`        | Create a group      |
| `DELETE` | `/api/groups/{id}`   | Delete a group      |

### System

| Method | Endpoint                | Description                                                |
|--------|-------------------------|------------------------------------------------------------|
| `GET`  | `/healthz`              | Health check (returns `healthy`)                           |
| `GET`  | `/api/version`          | Current build version                                      |
| `GET`  | `/openapi/v1.json`      | OpenAPI specification                                      |
| `GET`  | `/scalar/v1`            | Scalar interactive API reference                           |
| `GET`  | `/metrics`              | Prometheus scrape endpoint (internal only)                 |
| `GET`  | `/logging`              | Recent structured log entries (internal only)              |

### Example: Generate a Deck

```bash
curl -X POST http://localhost:5000/api/decks/generate \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <your-jwt-token>" \
  -d '{
    "colors": ["B", "G"],
    "format": "Commander",
    "powerLevel": "Casual",
    "budgetRange": "Budget (under $50)",
    "preferredStrategy": "Aristocrats",
    "preferredCommander": "Meren of Clan Nel Toth"
  }'
```

## Configuration

### Environment Variables

| Variable                      | Description                                      | Default / Notes                        |
|-------------------------------|--------------------------------------------------|----------------------------------------|
| `ANTHROPIC_API_KEY`           | Anthropic API key for Claude                     | *(required)*                           |
| `ADMIN_PASSWORD`              | Password for the seeded admin account            | *(required in production)*             |
| `JWT_SECRET`                  | Secret key for signing JWT tokens (≥ 32 chars)   | *(required in production)*             |
| `DATABASE_URL`                | PostgreSQL connection URI (Railway format)        | Falls back to `SqlStorage:ConnectionString` |
| `MongoDb__ConnectionString`   | MongoDB connection string                        | `mongodb://mongodb:27017`              |
| `MongoDb__DatabaseName`       | MongoDB database name                            | `mtgdeckforge`                         |
| `ClaudeApi__Model`            | Claude model to use                              | `claude-sonnet-4-20250514`             |
| `ClaudeApi__MaxTokens`        | Max tokens per generation request                | `16384`                                |
| `LOKI_URL`                    | Grafana Loki endpoint for log push               | *(optional)*                           |
| `CORS_ALLOWED_ORIGINS`        | Comma-separated allowed origins (production)     | Wide-open in development               |
| `GRAFANA_ADMIN_PASSWORD`      | Grafana admin password                           | `admin` (change in production)         |
| `PORT`                        | Port the API listens on                          | `5000`                                 |

### Local Development Config (`appsettings.json`)

For local development, the `SqlStorage:ConnectionString` defaults to `Host=localhost;Port=5432;...`. Use `docker-compose-local.yml` to spin up local dependencies (MongoDB on :27018, PostgreSQL on :5433):

```bash
docker compose -f docker-compose-local.yml up -d

cd MtgDeckForge.Api
export ANTHROPIC_API_KEY=sk-ant-xxxxx
dotnet run
```

Default login page is at `/Account/Login`.

## Observability

The application includes a full observability stack:

- **Health Check**: `GET /healthz` — returns `{ status: "healthy" }`
- **Prometheus Metrics**: Scraped from `/metrics` (ASP.NET Core + HTTP client + runtime instrumentation via OpenTelemetry)
- **Grafana Loki**: Set `LOKI_URL` to push structured logs directly to a Loki instance; labels are set to `app=MtgDeckForge`
- **Grafana**: Pre-configured dashboards at `http://localhost:3000` (admin/admin locally)
- **Structured Logging**: Serilog with console output; recent logs available via `GET /logging`
- **Application Logs**: `docker compose logs -f mtg-api`

The `/metrics` and `/logging` endpoints are restricted to Docker-internal IPs and are not publicly accessible.

### Pricing refresh

- Automatic price refresh runs daily via a hosted background service (`PricingRefreshHostedService`).
- Admin users can trigger a manual refresh:

```bash
curl -X POST http://localhost:5000/api/pricing/refresh \
  -H "Authorization: Bearer <admin-jwt-token>"
```

## Project Structure

```
MtgDeckForge/
├── docker-compose.yml              # Production: API + MongoDB + Prometheus + Grafana
├── docker-compose-local.yml        # Local dev: MongoDB + PostgreSQL + Prometheus + Grafana
├── Dockerfile                      # Multi-stage .NET 10 build (sdk:10.0 → aspnet:10.0)
├── .env.example                    # Environment variable template
├── MtgDeckForge.sln                # Solution file
├── monitoring/                     # Prometheus & Grafana config
│   ├── prometheus/
│   └── grafana/
├── MtgDeckForge.Tests/             # xUnit test project
│   ├── ClaudeServiceTests.cs
│   ├── ScryfallServiceTests.cs
│   └── DecksControllerCsvHelpersTests.cs
└── MtgDeckForge.Api/
    ├── MtgDeckForge.Api.csproj     # Project file (net10.0; Scalar, OpenAPI, MongoDB.Driver 3.x)
    ├── Program.cs                  # Service registration & middleware pipeline
    ├── appsettings.json            # Default configuration
    ├── Controllers/
    │   ├── DecksController.cs      # Deck CRUD, generate, analyze, CSV import/export
    │   ├── PricingController.cs    # Card price search, lookup, import management
    │   ├── AuthController.cs       # Login, user management
    │   └── GroupsController.cs     # Group management (Admin)
    ├── Data/
    │   └── AppDbContext.cs         # EF Core DbContext (Identity + pricing tables)
    ├── Json/
    │   └── AppJsonContext.cs       # Source-generated JSON serialization context (AOT-ready)
    ├── Migrations/                 # EF Core PostgreSQL migrations
    ├── Models/
    │   ├── DeckModels.cs           # Deck, Card, Request/Response models
    │   ├── UserModels.cs           # User, Group, Auth request/response models
    │   ├── MongoDbSettings.cs      # MongoDB config POCO
    │   ├── ClaudeApiSettings.cs    # Claude API config POCO
    │   ├── JwtSettings.cs          # JWT config POCO
    │   └── CardPrice.cs            # EF Core pricing entity
    ├── Observability/              # InMemoryLogStore, InMemoryLogSink, middleware
    ├── Pages/                      # Razor Pages (login, logout, deck views)
    │   ├── Account/Login.cshtml
    │   ├── Account/Logout.cshtml
    │   └── Decks/
    ├── Services/
    │   ├── DeckService.cs                  # MongoDB deck CRUD
    │   ├── ClaudeService.cs                # Claude AI integration
    │   ├── ScryfallService.cs              # Scryfall card enrichment & color derivation
    │   ├── PricingService.cs               # Apply local prices to card lists
    │   ├── MtgJsonPricingImportService.cs  # MTGJSON bulk price import
    │   ├── PricingRefreshHostedService.cs  # Daily background price refresh (IHostedLifecycleService)
    │   ├── AuthService.cs                  # JWT generation, password hashing
    │   └── UserService.cs                  # MongoDB user/group CRUD + seeding
    └── wwwroot/
        └── index.html                      # MTG-themed SPA frontend
```

## Development

```bash
# Build
dotnet build MtgDeckForge.sln

# Run tests
dotnet test MtgDeckForge.sln

# Run locally (requires MongoDB and PostgreSQL — use docker-compose-local.yml)
cd MtgDeckForge.Api && dotnet run
```

## License

MIT
