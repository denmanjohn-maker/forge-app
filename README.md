# ⚔️ mtg-forge

A best-of-breed Magic: The Gathering deck analysis and generation tool powered by a full RAG pipeline — Qdrant vector search + a hosted LLM — deployed on [Railway](https://railway.app). Built with .NET 10, MongoDB, PostgreSQL, Scryfall, and MTGJSON.

![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)
![MongoDB](https://img.shields.io/badge/MongoDB-7-green)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-blue)
![Railway](https://img.shields.io/badge/Deployed%20on-Railway-blueviolet)

---

## Table of Contents

1. [Why This Tool is Different](#why-this-tool-is-different)
2. [Full Architecture on Railway](#full-architecture-on-railway)
3. [The Full Chain — How a Deck Gets Built](#the-full-chain--how-a-deck-gets-built)
4. [Card Ingestion — The Foundation](#card-ingestion--the-foundation)
5. [Pricing Data Pipeline](#pricing-data-pipeline)
6. [Scryfall Enrichment](#scryfall-enrichment)
7. [Budget Enforcement Loop](#budget-enforcement-loop)
8. [Authentication & Multi-User Support](#authentication--multi-user-support)
9. [Observability Stack](#observability-stack)
10. [All API Endpoints](#all-api-endpoints)
11. [Railway Deployment Guide](#railway-deployment-guide)
12. [Environment Variables Reference](#environment-variables-reference)
13. [Local Development](#local-development)
14. [Project Structure](#project-structure)

---

## Why This Tool is Different

Most AI deck generators ask a language model to invent a 100-card list from memory. That approach has two fundamental problems:

1. **Card prices are hallucinated.** LLMs estimate prices from training data that is months or years old. A deck requested at "$50 budget" routinely exceeds $200.
2. **Card legality is guessed.** LLMs suggest cards banned in the format or not in the correct color identity.

**mtg-forge fixes both problems structurally:**

- Real MTG card data (~30,000 cards) is pulled from Scryfall and stored in a Qdrant vector database with price, color identity, and format legality as filterable payload fields.
- When a deck is requested, Qdrant pre-filters the candidate card pool **before** the LLM sees it — the LLM never gets to suggest a card that is illegal or over budget.
- Prices shown in the app come from the MTGJSON daily pricing feed, not from LLM memory.
- After generation, a post-generation budget enforcement loop swaps any card that still exceeds the per-card price ceiling using real prices from the local PostgreSQL cache.

---

## Full Architecture on Railway

```
Railway project
├── mtg-api              (.NET 10 — this repo, public ingress)
│     ├── Razor Pages (login, admin)
│     ├── REST API (decks, auth, pricing, groups)
│     ├── SPA (wwwroot/index.html — no build step)
│     └──► mtg-forge-ai.railway.internal:8080
│
├── mtg-forge-ai         (.NET — separate repo, internal only)
│     ├── /api/decks/generate  ← receives requests from mtg-api
│     ├── CardSearchService  ──► qdrant.railway.internal:6333
│     └── LLM calls          ──► api.together.xyz (Together.ai)
│
├── qdrant               (Docker: qdrant/qdrant, volume on /qdrant/storage)
│     └── "cards" collection — ~30k vectors, 384 dimensions
│         payload per card: name, price, color_identity, format legality flags
│
├── mongodb              (deck documents + users + groups)
│     └── database: mtgdeckforge
│         collections: decks, users, groups
│
└── postgresql           (ASP.NET Identity + MTGJSON pricing cache)
      └── tables: AspNetUsers, AspNetRoles, CardPrices, PricingImportRuns
```

**Service visibility:**
- `mtg-api` is the only service with a public Railway domain. All others communicate over Railway's private internal network.
- `mtg-forge-ai` and `qdrant` are never exposed to the internet.

---

## The Full Chain — How a Deck Gets Built

This is the complete request path from browser click to saved deck:

```
Browser / SPA (wwwroot/index.html)
  │
  │  POST /api/decks/generate  (JWT bearer token)
  ▼
DecksController.Generate (mtg-forge.Api)
  │
  │  Rate limit: 20 generations per user per 24 hours
  │
  ├─► RagPipelineService.GenerateDeckAsync
  │     │
  │     │  POST http://mtg-forge-ai.railway.internal:8080/api/decks/generate
  │     │  Body: { format, theme/strategy, budget (numeric), powerLevel,
  │     │          commander, colorIdentity, extraContext }
  │     ▼
  │   mtg-forge-ai service (separate repo)
  │     │
  │     ├─► CardSearchService → Qdrant
  │     │     Query: semantic search on theme/strategy
  │     │     Filter: price ≤ budget ceiling, color identity, format legality
  │     │     Returns: top-N candidate cards that already pass all hard constraints
  │     │
  │     ├─► LLM (Together.ai — meta-llama/Llama-3.3-70B-Instruct-Turbo)
  │     │     Input: candidate cards + format rules + deck request
  │     │     Output: 100-card deck list as structured JSON
  │     │
  │     └─► Returns LocalDeckResponse to mtg-api
  │
  ├─► PricingService.ApplyPricesAsync
  │     Looks up every card in the PostgreSQL price cache (MTGJSON data)
  │     Overwrites EstimatedPrice on each CardEntry with the real daily price
  │
  ├─► Budget enforcement loop (up to 3 passes)
  │     If deck total > budget max:
  │       - Identify cards exceeding per-card ceiling
  │       - Call RagPipelineService.SuggestBudgetReplacementsAsync
  │       - Swap expensive cards for cheaper alternatives
  │       - Re-apply real prices
  │
  ├─► ScryfallService.EnrichCardsAsync (CSV import path only)
  │     Fills mana cost, CMC, card type for any card missing those fields
  │
  ├─► DeckService.CreateAsync
  │     Persists the finished DeckConfiguration document to MongoDB
  │
  └─► HTTP 201 Created — full deck JSON returned to client
```

---

## Card Ingestion — The Foundation

Before any deck can be generated, the Qdrant vector database must be populated with real card data. This is a one-time setup step (plus occasional re-runs when new sets are released).

### What ingestion does

1. **Downloads the Scryfall bulk data file** (~250MB JSON with every printed card).
2. **For each card**, extracts:
   - Name
   - Color identity (`W`, `U`, `B`, `R`, `G` or colorless)
   - Format legality flags (`legality_commander`, `legality_standard`, `legality_modern`, `legality_legacy`, `legality_pioneer`, `legality_pauper`, `legality_vintage`)
   - Market price (USD)
   - Oracle text / type line
3. **Generates a 384-dimension embedding** for each card using the `all-minilm` model served by Ollama (running in the same Railway project).
4. **Upserts each card into Qdrant** as a vector point with all the above fields as filterable payload.
5. **Writes card records to MongoDB** (optional `mongoOnly` flag) for reference lookups.

A full ingest of ~30,000 cards takes 20–60 minutes. A smoke-test run with `"limit": 1000` takes 2–3 minutes.

### Running ingestion on Railway

Ingestion is triggered via the Railway shell inside the `mtg-forge-ai` service (Dashboard → mtg-forge-ai → Deploy tab → Shell):

```bash
# Full ingest — all ~30k cards (takes 20-60+ min)
curl -X POST http://localhost:8080/api/admin/ingest \
  -H "Content-Type: application/json" \
  -d '{}'

# Quick smoke test — 1000 cards (~2-3 min)
curl -X POST http://localhost:8080/api/admin/ingest \
  -H "Content-Type: application/json" \
  -d '{"limit": 1000}'
```

Optional body fields:

| Field        | Type   | Description                                               |
|--------------|--------|-----------------------------------------------------------|
| `limit`      | `int?` | Cap the number of cards ingested (omit for all ~30k)      |
| `mongoOnly`  | `bool` | Only write to MongoDB, skip Qdrant embedding              |
| `qdrantOnly` | `bool` | Only (re-)embed into Qdrant from existing MongoDB records |

### Verifying the collection

```bash
# From any Railway shell
curl http://qdrant.railway.internal:6333/collections/cards
```

A healthy collection shows `"vectors_count"` in the tens of thousands:

```json
{
  "result": {
    "status": "green",
    "vectors_count": 28000
  }
}
```

### Embedding model note

The embedding model (`all-minilm`, 384 dimensions) must match what was used when the collection was first created. Qdrant collections are **immutable in vector dimension** — switching models requires deleting the collection and re-ingesting:

```bash
curl -X DELETE http://qdrant.railway.internal:6333/collections/cards
# Then re-run ingestion
```

---

## Pricing Data Pipeline

Card prices in mtg-forge come from two sources that serve different purposes:

### 1. MTGJSON daily pricing feed (local cache — authoritative for display)

**What it does:** Every 24 hours, a background service streams the MTGJSON `AllPricesToday.json` and `AllPrintings.json` feeds, resolves card UUIDs to names, and upserts prices into the PostgreSQL `CardPrices` table.

**Why streaming:** The MTGJSON files are very large (hundreds of MB). The importer uses `Utf8JsonReader` to parse them incrementally without loading them fully into memory, preventing OOM on Railway's constrained containers.

**Why PostgreSQL:** SQL batch upserts (~1,000 rows per batch) are efficient and PostgreSQL is already present for ASP.NET Identity. MongoDB would work too but SQL gives clean atomic upsert semantics.

**How it's used:** After `RagPipelineService` generates a deck, `PricingService.ApplyPricesAsync` queries the PostgreSQL cache and overwrites the `EstimatedPrice` on every `CardEntry` with today's real market price. This means the prices shown in the app are always based on fresh MTGJSON data, not LLM guesses.

**Manual trigger (admin only):**

```bash
curl -X POST https://<your-railway-domain>/api/pricing/refresh \
  -H "Authorization: Bearer <admin-jwt>"
```

### 2. Qdrant payload prices (used during generation for pre-filtering)

During ingestion, the price ingested for each card into Qdrant is also a real price pulled from Scryfall bulk data. This price is used by the `CardSearchService` inside `mtg-forge-ai` as a hard filter: cards above the budget ceiling are excluded from the candidate pool before the LLM sees them.

This means budget compliance is enforced **twice**: once in Qdrant (structural pre-filter) and once in `DecksController` (post-generation budget loop using the MTGJSON cache).

---

## Scryfall Enrichment

`ScryfallService.EnrichCardsAsync` fills in missing card metadata for decks that arrive without full data — primarily the **CSV import** path.

- Calls Scryfall's `/cards/collection` endpoint in batches of 75 (Scryfall's API limit).
- Fills `ManaCost`, `Cmc`, `CardType`, and `EstimatedPrice` for any card where those fields are empty.
- **Non-destructive**: fields already populated by the LLM or RAG pipeline are never overwritten.
- Sets a `User-Agent: mtg-forge/1.0` header as required by Scryfall's API terms.

This step runs automatically during CSV import. It does not run during RAG generation (the RAG pipeline provides full card data directly).

---

## Budget Enforcement Loop

Even with Qdrant pre-filtering, real MTGJSON prices can differ from the prices ingested during the last ingest run. The post-generation enforcement loop catches any remaining overage:

1. Compare `deck.EstimatedTotalPrice` (using real MTGJSON prices) against the budget max for the selected tier.
2. If over budget, identify all cards exceeding the per-card price ceiling (scaled by tier: $1 for Budget, $2 for Mid, $5 for Focused/High).
3. Call `SuggestBudgetReplacementsAsync` to get cheaper alternatives from the MTGJSON price cache.
4. Swap cards in-place (respecting the Commander singleton rule — no duplicate card names).
5. Re-apply real prices and re-check. Repeats up to 3 times.

Budget tiers:

| Tier              | Max Total | Per-Card Ceiling |
|-------------------|-----------|-----------------|
| Budget            | $50       | $1.00           |
| Mid-range         | $150      | $2.00           |
| Focused / High    | >$150     | $5.00           |

---

## Authentication & Multi-User Support

The app uses a **dual authentication scheme** — JWT for API clients, cookie session for Razor Pages — resolved by a `"smart"` policy scheme at the middleware level:

- Requests with `Authorization: Bearer <token>` → JWT bearer validation
- All other requests → ASP.NET Identity cookie (Razor Pages login flow)

**User accounts** are stored in two places:
- **PostgreSQL** (`AspNetUsers`, `AspNetRoles`): ASP.NET Identity for login, password hashing, and role management
- **MongoDB** (`users` collection): `UserService` stores display names and owns deck associations

**Roles:**
- `Admin` — can view and manage all users' decks, trigger pricing refresh, access `/logging`
- `User` — can only access their own decks

**Rate limiting:** Deck generation is capped at **20 requests per user per 24 hours** using ASP.NET Core's fixed-window rate limiter, keyed by the user's identity claim. Anonymous requests share one bucket.

**Groups:** `GroupsController` (admin-only) supports creating user groups for deck sharing.

---

## Observability Stack

### Health check

```
GET /healthz → { "status": "healthy" }
```

Used by Railway's health probe and the Dockerfile `HEALTHCHECK` directive.

### Structured logging (Serilog)

All logs are written to:
- Console (structured format, visible in Railway's log viewer)
- An in-memory ring buffer (last 1,000 entries, accessible at `/logging`)
- OpenTelemetry OTLP exporter (configurable via `OTEL_EXPORTER_OTLP_ENDPOINT`)

The `/logging` endpoint is protected by `InternalOnlyMiddleware` — only accessible from Docker-internal IPs (not from the public internet).

### Prometheus metrics

```
GET /metrics
```

Exposes ASP.NET Core request metrics, HTTP client metrics, and .NET runtime metrics in Prometheus format. Also protected by `InternalOnlyMiddleware`.

The `docker-compose.yml` includes a Prometheus container pre-configured to scrape `/metrics` and a Grafana container with a pre-provisioned datasource.

### Version endpoint

```
GET /api/version → { "version": "2025.04.23.1530" }
```

Returns the `BUILD_VERSION` environment variable if set, otherwise derives a version from the assembly's last write time.

---

## All API Endpoints

### Decks

| Method    | Endpoint                     | Auth      | Description                                      |
|-----------|------------------------------|-----------|--------------------------------------------------|
| `GET`     | `/api/decks`                 | User      | List decks (paginated, filterable by name/color/format/power) |
| `GET`     | `/api/decks/{id}`            | User      | Get a specific deck                              |
| `GET`     | `/api/decks/search`          | User      | Search by `?color=B&format=Commander`            |
| `POST`    | `/api/decks/generate`        | User      | Generate a new deck via the RAG pipeline         |
| `PATCH`   | `/api/decks/{id}`            | User      | Update deck metadata or card list                |
| `DELETE`  | `/api/decks/{id}`            | User      | Delete a deck                                    |
| `POST`    | `/api/decks/{id}/copy`       | User      | Duplicate a deck                                 |
| `POST`    | `/api/decks/{id}/analyze`    | User      | AI analysis: synergy, weaknesses, upgrade suggestions |
| `GET`     | `/api/decks/{id}/export/csv` | User      | Export as CSV (`?format=moxfield\|archidekt\|deckbox\|deckstats`) |
| `POST`    | `/api/decks/import/csv`      | User      | Import a CSV deck list (auto-detects format)     |
| `GET`     | `/api/decks/metrics`         | User      | Aggregate metrics for the current user's decks   |

### Auth

| Method  | Endpoint                    | Auth  | Description                        |
|---------|-----------------------------|-------|------------------------------------|
| `POST`  | `/api/auth/login`           | None  | Login, returns JWT + cookie        |
| `POST`  | `/api/auth/register`        | None  | Register a new account             |
| `POST`  | `/api/auth/logout`          | User  | Invalidate cookie session          |
| `GET`   | `/api/auth/me`              | User  | Current user profile               |
| `PATCH` | `/api/auth/me`              | User  | Update display name / password     |

### Pricing (Admin)

| Method  | Endpoint               | Auth  | Description                              |
|---------|------------------------|-------|------------------------------------------|
| `POST`  | `/api/pricing/refresh` | Admin | Manually trigger MTGJSON pricing refresh |
| `GET`   | `/api/pricing/status`  | Admin | Last import run details                  |

### Groups (Admin)

| Method   | Endpoint              | Auth  | Description          |
|----------|-----------------------|-------|----------------------|
| `GET`    | `/api/groups`         | Admin | List all groups      |
| `POST`   | `/api/groups`         | Admin | Create a group       |
| `DELETE` | `/api/groups/{id}`    | Admin | Delete a group       |

### System

| Method | Endpoint       | Auth     | Description                      |
|--------|----------------|----------|----------------------------------|
| `GET`  | `/healthz`     | None     | Health check                     |
| `GET`  | `/api/version` | None     | Build version                    |
| `GET`  | `/metrics`     | Internal | Prometheus metrics scrape target |
| `GET`  | `/logging`     | Internal | Recent structured log entries    |

---

## Railway Deployment Guide

### Services to create in your Railway project

| Service         | Source                          | Hostname (internal)          | Public? |
|-----------------|---------------------------------|------------------------------|---------|
| `mtg-api`       | This repo (GitHub)              | `mtg-api.railway.internal`   | ✅ Yes  |
| `mtg-forge-ai`  | `mtg-forge-card-ai` repo        | `mtg-forge-ai.railway.internal` | ❌ No |
| `qdrant`        | Docker image `qdrant/qdrant`    | `qdrant.railway.internal`    | ❌ No   |
| `ollama`        | Docker image `ollama/ollama`    | `ollama.railway.internal`    | ❌ No   |
| `mongodb`       | Railway MongoDB plugin          | (Railway-provided)           | ❌ No   |
| `postgresql`    | Railway PostgreSQL plugin       | (Railway-provided)           | ❌ No   |

### Step 1 — Deploy Qdrant

1. New service → Docker Image → `qdrant/qdrant`
2. Set internal hostname: `qdrant`
3. Add a volume at `/qdrant/storage` (persists vectors across deploys)
4. No public ingress

### Step 2 — Deploy Ollama and pull models

1. New service → Docker Image → `ollama/ollama`
2. Set internal hostname: `ollama`
3. Add a volume at `/root/.ollama` (persists downloaded models)
4. No public ingress
5. After deploy, open the Railway shell and pull both required models:

```bash
# Embedding model — used during card ingestion
ollama pull all-minilm

# Generation model — used for deck analysis descriptions
ollama pull mistral

# Verify
ollama list
```

> Both models must be present. `all-minilm` produces 384-dimension embeddings for Qdrant. `mistral` is used for deck analysis and import description generation.

### Step 3 — Deploy mtg-forge-ai

1. New service → GitHub repo → `mtg-forge-card-ai`
2. Set internal hostname: `mtg-forge-ai`
3. No public ingress (only `mtg-api` calls it)
4. Set environment variables:

| Variable                   | Value                                    |
|----------------------------|------------------------------------------|
| `Qdrant__Host`             | `qdrant.railway.internal`                |
| `Qdrant__Port`             | `6334`                                   |
| `Ollama__BaseUrl`          | `http://ollama.railway.internal:11434`   |
| `Ollama__Model`            | `mistral`                                |
| `Ollama__EmbedModel`       | `all-minilm`                             |
| `MongoDB__ConnectionString`| `<Railway MongoDB internal URL>`         |
| `MongoDB__DatabaseName`    | `mtgforge`                               |

### Step 4 — Run card ingestion (one-time)

From the Railway shell inside `mtg-forge-ai` (Dashboard → mtg-forge-ai → Deploy → Shell):

```bash
# Full ingest
curl -X POST http://localhost:8080/api/admin/ingest \
  -H "Content-Type: application/json" \
  -d '{}'
```

Watch the `mtg-forge-ai` service logs for progress. Expect 20–60 minutes.

Verify:

```bash
curl http://qdrant.railway.internal:6333/collections/cards
# Look for "vectors_count": ~28000
```

### Step 5 — Deploy mtg-api (this repo)

1. New service → GitHub repo → this repo
2. Enable public ingress (this is the user-facing domain)
3. Railway auto-injects `DATABASE_URL` and `PORT`
4. Set environment variables:

| Variable                    | Value                                           | Notes                              |
|-----------------------------|-------------------------------------------------|------------------------------------|
| `LlmProvider`               | `Rag`                                           | Routes to RAG pipeline             |
| `RagPipeline__BaseUrl`      | `http://mtg-forge-ai.railway.internal:8080`     | mtg-forge-ai internal DNS          |
| `RagPipeline__LlmBaseUrl`   | `https://api.together.xyz`                      | Together.ai API base               |
| `RagPipeline__LlmApiKey`    | `<your Together.ai API key>`                    | Required for deck analysis         |
| `RagPipeline__Model`        | `meta-llama/Llama-3.3-70B-Instruct-Turbo`       | LLM for analysis + import descriptions |
| `MongoDb__ConnectionString` | `<Railway MongoDB internal URL>`                | Deck + user storage                |
| `DATABASE_URL`              | *(Railway auto-injects)*                        | PostgreSQL for Identity + pricing  |
| `JWT_SECRET`                | A long random string (32+ chars)                | JWT signing key                    |
| `ADMIN_PASSWORD`            | A strong password                               | Seeds the initial admin account    |

### Step 6 — Verify

```bash
# Health check
curl https://<your-railway-domain>/healthz
# → {"status":"healthy"}

# Generate a deck (requires a JWT from /api/auth/login first)
curl -X POST https://<your-railway-domain>/api/decks/generate \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <jwt>" \
  -d '{
    "colors": ["B", "G"],
    "format": "Commander",
    "powerLevel": "Focused",
    "budgetRange": "Budget",
    "preferredCommander": "Meren of Clan Nel Toth",
    "preferredStrategy": "sacrifice and reanimation"
  }'
```

Generation takes 30–90 seconds (Qdrant search + LLM inference).

---

## Environment Variables Reference

### mtg-api (this repo)

| Variable                    | Description                                  | Default / Notes                                |
|-----------------------------|----------------------------------------------|------------------------------------------------|
| `LlmProvider`               | `Rag` routes to mtg-forge-ai                 | `Rag`                                          |
| `RagPipeline__BaseUrl`      | mtg-forge-ai internal URL                    | `http://mtg-forge-ai.railway.internal:8080`    |
| `RagPipeline__LlmBaseUrl`   | Together.ai (or any OpenAI-compat) base URL  | `https://api.together.xyz`                     |
| `RagPipeline__LlmApiKey`    | Together.ai API key                          | *(required for analysis)*                      |
| `RagPipeline__Model`        | LLM model name for analysis                  | `meta-llama/Llama-3.3-70B-Instruct-Turbo`      |
| `MongoDb__ConnectionString` | MongoDB connection string                    | `mongodb://mongodb:27017`                      |
| `MongoDb__DatabaseName`     | MongoDB database name                        | `mtgdeckforge`                                 |
| `DATABASE_URL`              | PostgreSQL URI (Railway auto-injects)        | Required                                       |
| `JWT_SECRET`                | JWT signing secret (min 32 chars)            | Required in production                         |
| `ADMIN_PASSWORD`            | Password for the seeded admin account        | Required in production                         |
| `PORT`                      | HTTP port (Railway auto-injects)             | `5000`                                         |
| `CORS_ALLOWED_ORIGINS`      | Comma-separated allowed origins              | Open in development                            |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OpenTelemetry collector endpoint           | `http://localhost:4317`                        |
| `BUILD_VERSION`             | Version string for `/api/version`            | Derived from assembly timestamp if unset       |

---

## Local Development

### Prerequisites

- .NET 10 SDK
- Docker (for local dependencies)
- A Together.ai API key (for deck analysis; free tier available)

### Start local dependencies

```bash
docker compose -f docker-compose-local.yml up -d
```

This starts:
- MongoDB on `localhost:27018`
- PostgreSQL on `localhost:5433`
- Prometheus on `localhost:9090`
- Grafana on `localhost:3000`

### Run the API

```bash
cd mtg-forge.Api
dotnet run
```

The app listens on `http://localhost:5000`. Scalar API reference is at `/scalar`.

### Apply migrations

```bash
cd mtg-forge.Api
dotnet ef database update
```

### Run tests

```bash
dotnet test mtg-forge.sln
```

### Using a local RAG pipeline

For local RAG generation, run `mtg-forge-ai` locally and set in `appsettings.json`:

```json
"LlmProvider": "Rag",
"RagPipeline": {
  "BaseUrl": "http://localhost:8080",
  "LlmBaseUrl": "https://api.together.xyz",
  "LlmApiKey": "<your-key>",
  "Model": "meta-llama/Llama-3.3-70B-Instruct-Turbo"
}
```

---

## Project Structure

```
mtg-forge/
├── Dockerfile                        # Multi-stage .NET 10 build (sdk → aspnet runtime)
├── docker-compose.yml                # Full stack: API + MongoDB + Prometheus + Grafana
├── docker-compose-local.yml          # Local dev dependencies only (no API container)
├── mtg-forge.sln
├── monitoring/
│   ├── prometheus/prometheus.yml     # Scrape config for /metrics endpoint
│   └── grafana/provisioning/         # Pre-configured Grafana datasource
└── mtg-forge.Api/
    ├── Program.cs                    # DI registration, middleware pipeline, startup seeding
    ├── appsettings.json              # Default config (overridden by env vars on Railway)
    ├── Controllers/
    │   ├── DecksController.cs        # Deck CRUD, generation, analysis, CSV import/export
    │   ├── AuthController.cs         # Login, register, JWT issuance
    │   ├── GroupsController.cs       # Admin: user group management
    │   └── PricingController.cs      # Admin: manual pricing refresh trigger
    ├── Services/
    │   ├── IDeckGenerationService.cs # Abstraction: GenerateDeck, AnalyzeDeck, SuggestReplacements
    │   ├── RagPipelineService.cs     # Calls mtg-forge-ai (Qdrant+LLM) + Together.ai directly
    │   ├── DeckService.cs            # MongoDB CRUD for DeckConfiguration documents
    │   ├── UserService.cs            # MongoDB CRUD for User documents
    │   ├── AuthService.cs            # Password hashing, JWT generation
    │   ├── ScryfallService.cs        # Card metadata enrichment (batched collection API)
    │   ├── PricingService.cs         # PostgreSQL price lookups + card name normalization
    │   ├── MtgJsonPricingImportService.cs  # Streaming MTGJSON import → PostgreSQL
    │   ├── PricingRefreshHostedService.cs  # Background service: daily pricing refresh
    │   ├── DeckMetricsCalculator.cs  # Mana curve, color distribution, category breakdown
    │   └── BudgetHelper.cs           # Budget tier → max price mapping
    ├── Models/
    │   ├── DeckModels.cs             # DeckConfiguration, CardEntry, DeckAnalysis, etc.
    │   ├── UserModels.cs             # User, Group
    │   ├── CardPrice.cs              # PostgreSQL entity for price cache
    │   ├── RagPipelineSettings.cs    # Config POCO for RAG pipeline
    │   ├── MongoDbSettings.cs
    │   ├── MtgJsonSettings.cs
    │   └── JwtSettings.cs
    ├── Data/
    │   └── AppDbContext.cs           # EF Core context: Identity + CardPrices + PricingImportRuns
    ├── Observability/
    │   ├── InMemoryLogStore.cs       # Ring buffer for recent log entries (/logging endpoint)
    │   ├── InMemoryLogSink.cs        # Serilog sink that writes to InMemoryLogStore
    │   └── InternalOnlyMiddleware.cs # Blocks /metrics and /logging from public internet
    ├── Pages/
    │   ├── Account/                  # Razor Pages: Login, Register
    │   └── Decks/                    # Razor Pages: admin deck management views
    └── wwwroot/
        └── index.html                # ~2000-line vanilla JS SPA (no build step)
                                      # MTG-themed UI, Scryfall card images, iOS/Safari compatible
```

---

## Troubleshooting

**Qdrant `vectors_count` is 0 after ingestion**
→ Check `mtg-forge-ai` logs. Common causes: Qdrant unreachable, Ollama model not pulled, embedding dimension mismatch. Delete the collection and re-ingest.

**"Ollama error NotFound: model not found"**
→ Shell into the Ollama Railway service and run `ollama pull all-minilm && ollama pull mistral`.

**Deck prices look wrong / cards over budget**
→ The MTGJSON pricing import may not have run yet. Trigger it manually: `POST /api/pricing/refresh` (admin). Check `/api/pricing/status` for the last run timestamp.

**"Generation limit reached"**
→ Rate limit: 20 deck generations per user per 24 hours. Wait for the window to reset, or use an admin account which is exempt.

**Slow first generation after deploy**
→ Ollama loads the model into RAM on the first request. Subsequent requests are faster. For `mistral` (7B), expect ~30s cold start on Railway's shared hardware.

**PostgreSQL migrations fail on fresh deploy**
→ The startup code detects stale migration history and automatically drops and re-runs migrations. If it still fails, check `DATABASE_URL` is set correctly.

---

## License

MIT
