# ⚔️ mtg-forge

A Magic: The Gathering deck configuration generator powered by Claude AI, built with .NET 8, MongoDB, SQL LocalDB pricing cache, and Docker.

![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)
![MongoDB](https://img.shields.io/badge/MongoDB-7-green)
![Docker](https://img.shields.io/badge/Docker-Compose-blue)

## Features

- **AI-Powered Deck Generation** — Uses the Anthropic Claude API to generate complete, format-legal deck configurations based on your parameters
- **Rich Configuration Options** — Choose mana colors, format (Commander/Standard/Modern/Pioneer), power level, budget range, strategy archetype, and more
- **Persistent Storage** — Decks are stored in MongoDB; authentication and price cache are stored in SQL LocalDB
- **Razor Pages UI** — Server-rendered login and deck workflows with ASP.NET Core Identity cookie auth
- **Daily MTGJSON Pricing Import** — Pulls MTGJSON bulk pricing daily and updates local price cache
- **Full CRUD** — Create (generate), Read (list/detail), Delete deck configurations via REST API
- **Swagger API Docs** — Available at `/swagger` for testing endpoints directly

## Architecture

```
┌─────────────────────────────────────────────┐
│              Docker Compose                  │
│                                              │
│  ┌──────────────────┐  ┌─────────────────┐  │
│  │  mtg-api          │  │  mongodb        │  │
│  │  .NET 8 Web API   │──│  Mongo 7        │  │
│  │  + Static SPA     │  │  Port 27017     │  │
│  │  Port 5000        │  │                 │  │
│  └────────┬─────────┘  └─────────────────┘  │
│           │                                  │
│           │ HTTP POST to Claude API          │
│           ▼                                  │
│     api.anthropic.com                        │
└─────────────────────────────────────────────┘
```

## Quick Start

### Prerequisites

- Docker & Docker Compose
- An [Anthropic API key](https://console.anthropic.com/)

### 1. Clone & Configure

```bash
git clone <your-repo-url>
cd mtg-forge

# Create your environment file
cp .env.example .env

# Edit .env and add your Anthropic API key
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

| Method   | Endpoint              | Description                          |
|----------|-----------------------|--------------------------------------|
| `GET`    | `/api/decks`          | List all saved deck configurations   |
| `GET`    | `/api/decks/{id}`     | Get a specific deck by ID            |
| `GET`    | `/api/decks/search`   | Search decks by `?color=B&format=Commander` |
| `POST`   | `/api/decks/generate` | Generate a new deck via Claude AI    |
| `DELETE` | `/api/decks/{id}`     | Delete a deck configuration          |

### Example: Generate a Deck

```bash
curl -X POST http://localhost:5000/api/decks/generate \
  -H "Content-Type: application/json" \
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

| Variable                    | Description                        | Default                      |
|-----------------------------|------------------------------------|------------------------------|
| `ANTHROPIC_API_KEY`         | Your Anthropic API key             | *(required)*                 |
| `MongoDb__ConnectionString` | MongoDB connection string          | `mongodb://mongodb:27017`    |
| `MongoDb__DatabaseName`     | Database name                      | `mtgdeckforge`               |
| `ClaudeApi__Model`          | Claude model to use                | `claude-sonnet-4-20250514`     |
| `ClaudeApi__MaxTokens`      | Max tokens for generation          | `8192`                       |

### Using with Authentication (Production)

Uncomment the MongoDB auth lines in `docker-compose.yml`:

```yaml
mongodb:
  environment:
    MONGO_INITDB_ROOT_USERNAME: admin
    MONGO_INITDB_ROOT_PASSWORD: ${MONGO_PASSWORD:-changeme}
```

Update the connection string accordingly:

```yaml
mtg-api:
  environment:
    MongoDb__ConnectionString: "mongodb://admin:${MONGO_PASSWORD}@mongodb:27017"
```

## Observability

The application exposes health checks and structured logging:

- **Health Check**: The API container includes a Docker healthcheck hitting `/swagger/index.html`
- **Logs**: `docker compose logs -f mtg-api` for application logs
- **MongoDB**: Accessible on `localhost:27017` for tools like MongoDB Compass

For integration with your existing observability stack, you can add Prometheus metrics with the `prometheus-net.AspNetCore` NuGet package and configure a Grafana dashboard.

## Development (Without Docker)

```bash
# Start MongoDB locally
mongod --dbpath ./data

# Run the API (Razor Pages + API)
cd mtg-forge.Api
export ANTHROPIC_API_KEY=sk-ant-xxxxx
dotnet run
```

Default login page is at `/Account/Login`.

### Pricing refresh

- Automatic refresh runs daily in a hosted background service.
- Admin can trigger manual refresh via:

```bash
curl -X POST http://localhost:5000/api/pricing/refresh --cookie "<auth-cookie>"
```

## Project Structure

```
mtg-forge/
├── docker-compose.yml          # Orchestration: API + MongoDB
├── Dockerfile                  # Multi-stage .NET 8 build
├── .env.example                # Environment template
├── mtg-forge.sln            # Solution file
└── mtg-forge.Api/
    ├── mtg-forge.Api.csproj # Project with MongoDB.Driver
    ├── Program.cs              # Service registration & middleware
    ├── appsettings.json        # Default configuration
    ├── Controllers/
    │   └── DecksController.cs  # REST API endpoints
    ├── Models/
    │   ├── DeckModels.cs       # Deck, Card, Request models
    │   ├── MongoDbSettings.cs  # MongoDB config POCO
    │   └── ClaudeApiSettings.cs# Claude API config POCO
    ├── Services/
    │   ├── DeckService.cs      # MongoDB CRUD operations
    │   └── ClaudeService.cs    # Claude API integration
    └── wwwroot/
        └── index.html          # MTG-themed SPA frontend
```

## License

MIT
