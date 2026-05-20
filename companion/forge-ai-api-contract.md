# forge-ai-api — API Contract

Quick reference for agents and developers working in forge-app. For the full source, see `companion/forge-ai-api/`.

> **AI Provider:** forge-ai-api uses **DeepInfra** (`meta-llama/Llama-3.3-70B-Instruct`) for LLM chat and `BAAI/bge-m3` (1024-dim) for embeddings in production. Local development uses Ollama.

---

## Service coordinates

| Environment | Base URL |
|-------------|----------|
| Railway (production) | `http://mtg-forge-ai.railway.internal:8080` |
| Local Docker | `http://localhost:5001` |

forge-app configures this via `RagPipeline__BaseUrl`.

---

## Endpoints used by forge-app

### `POST /api/decks/generate`

The only endpoint forge-app calls during the deck generation flow (`RagPipelineService.GenerateDeckAsync`).

**Request body** (`DeckRequest`):

```json
{
  "format": "commander",
  "theme": "balanced",
  "budget": 150.0,
  "powerLevel": 7,
  "commander": "Atraxa, Praetors' Voice",
  "colorIdentity": ["W", "U", "B", "G"],
  "extraContext": "focus on proliferate synergies",
  "useMetaSignals": true
}
```

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `format` | string | yes | `commander`, `standard`, `modern`, `legacy`, `pioneer`, `pauper`, `vintage` |
| `theme` | string | yes | e.g. `balanced`, `aggro`, `control`. Mapped from `DeckGenerationRequest.PreferredStrategy`. |
| `budget` | number | yes | Total USD budget. Mapped from `DeckGenerationRequest.BudgetRange` via `MapBudget()`. |
| `powerLevel` | int | yes | 1–10. Mapped from `DeckGenerationRequest.PowerLevel` via `MapPowerLevel()`. |
| `commander` | string | no | Preferred commander name. If omitted for Commander format, the LLM chooses one. |
| `colorIdentity` | string[] | no | Required for Commander. Color filter for other formats. |
| `extraContext` | string | no | Free-form notes, including Universes Beyond addenda from `ThemedSetDetector`. |
| `useMetaSignals` | bool | no | Incorporate tournament meta-signal data. Defaults to `true`. |

**Response body** (`DeckResponse`):

```json
{
  "commander": "Atraxa, Praetors' Voice",
  "theme": "Proliferate Superfriends",
  "format": "commander",
  "sections": [
    {
      "category": "Ramp",
      "cards": [
        {
          "name": "Sol Ring",
          "quantity": 1,
          "priceUsd": 2.50,
          "oracleText": "{T}: Add {C}{C}.",
          "imageUri": "https://...",
          "scryfallUri": "https://...",
          "manaCost": "{1}",
          "cmc": 1,
          "typeLine": "Artifact"
        }
      ]
    }
  ],
  "estimatedCost": 147.32,
  "reasoning": "...",
  "generatedAt": "2024-01-15T12:00:00Z",
  "validationWarnings": []
}
```

| Field | Type | Notes |
|-------|------|-------|
| `commander` | string? | Commander card name (Commander format only) |
| `theme` | string | Theme/strategy name |
| `format` | string | Format echoed back |
| `sections` | DeckSection[] | Card list grouped by category (Ramp, Card Draw, Removal, Win Conditions, Lands, etc.) |
| `estimatedCost` | number | Total estimated USD cost |
| `reasoning` | string | LLM's explanation of card choices |
| `generatedAt` | datetime | UTC timestamp |
| `validationWarnings` | string[]? | Non-fatal issues detected post-generation |

**Response headers** (token usage tracking):

| Header | Value |
|--------|-------|
| `X-GenAI-Input-Tokens` | Prompt token count (present when > 0) |
| `X-GenAI-Output-Tokens` | Completion token count (present when > 0) |

forge-app reads these headers via `localDeck.InputTokens` / `localDeck.OutputTokens` for `AiUsageService` logging.

---

### `GET /api/health`

Used for readiness checks.

**Response:**
```json
{
  "status": "healthy",
  "llm": "ok",
  "mongodb": "ok",
  "qdrant": "ok",
  "timestamp": "2024-01-15T12:00:00Z"
}
```

`status` is `"healthy"` or `"degraded"` depending on LLM availability.

---

### `POST /api/admin/ingest`

Triggers card ingestion from Scryfall into MongoDB + Qdrant. Long-running; returns `202 Accepted` immediately.

**Request body** (all fields optional):
```json
{
  "mongoOnly": false,
  "qdrantOnly": false,
  "limit": null
}
```

**Response:** `202 Accepted`
```json
{ "message": "Ingestion started in the background. Check application logs for progress and results." }
```

Poll `GET /api/admin/ingest-status` for progress.

---

### `POST /api/cards/search`

Semantic card search via Qdrant. Not called by forge-app directly but useful for debugging card pool issues.

**Request body** (`CardSearchRequest`):
```json
{
  "query": "ramp that costs 2 or less",
  "colors": ["G"],
  "maxPrice": 5.0,
  "limit": 20,
  "format": "commander"
}
```

---

## Internal pipeline (inside forge-ai-api)

When `POST /api/decks/generate` is called:

1. **Embed** the generation prompt → DeepInfra `BAAI/bge-m3` (1024-dim)
2. **Qdrant ANN search** — retrieve ~200 candidate cards filtered by format legality + color identity
3. Optionally **inject meta-signals** (tournament inclusion rates from MongoDB `meta_signals` collection)
4. **LLM chat** → DeepInfra `meta-llama/Llama-3.3-70B-Instruct` generates deck JSON
5. **Parse + validate** deck response (strip markdown fences, validate card counts)
6. **Save** to forge-ai-api's own MongoDB (`mtgforge` db, `decks` collection)
7. Return `DeckResponse` to forge-app

---

## Environment variables (forge-ai-api)

| Variable | Production value | Notes |
|----------|-----------------|-------|
| `LLM__Provider` | `openai` | `"openai"` = DeepInfra; `"ollama"` = local |
| `LLM__ApiKey` | DeepInfra API key | |
| `LLM__BaseUrl` | `https://api.deepinfra.com/v1/openai` | Any OpenAI-compatible endpoint |
| `LLM__Model` | `meta-llama/Llama-3.3-70B-Instruct` | |
| `LLM__EmbedModel` | `BAAI/bge-m3` | |
| `MongoDB__ConnectionString` | MongoDB URL | `mtgforge` db |
| `Qdrant__Host` | Qdrant hostname | |
| `Qdrant__Port` | `6334` | gRPC |

---

## Updating the submodule

When forge-ai-api changes its API contract, bump the submodule pointer in forge-app:

```bash
cd companion/forge-ai-api && git pull origin main
cd ../..
git add companion/forge-ai-api
git commit -m "chore: bump forge-ai-api submodule to <sha>"
```
