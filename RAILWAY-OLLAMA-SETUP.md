# Railway Ollama Setup Guide

This guide covers deploying MtgDeckForge on Railway with a hosted Ollama service instead of the Anthropic Claude API.

---

## Architecture

```
Railway
├── MtgDeckForge API (this repo)
│     └──► Ollama service (internal: ollama.railway.internal:11434)
├── MongoDB
└── PostgreSQL
```

The API uses `OllamaService` — a direct Ollama integration that replicates the same prompt structure as `ClaudeService` but targets the Ollama `/api/chat` endpoint.

---

## 1. Deploy Ollama on Railway

If you haven't already:

1. Go to your Railway project
2. Add a new service → **Docker Image** → `ollama/ollama`
3. Under the service settings:
   - Set **Internal Networking** hostname to `ollama` (this makes it reachable at `ollama.railway.internal`)
   - Add a **Volume** mount at `/root/.ollama` to persist downloaded models across deploys
4. Deploy the service

### Pull a model

After the Ollama service is running, open the Railway shell for the Ollama service and run:

```bash
ollama pull mistral
```

> **Note:** `mistral` (~4.1GB) is the default model. You can use any model — just update the `Ollama__Model` env var on the API service to match. Other good options:
> - `llama3.1:8b` (~4.7GB) — slightly better reasoning
> - `phi3:mini` (~2.3GB) — faster, lighter
> - `gemma2:9b` (~5.5GB) — good quality

Verify the model is available:
```bash
ollama list
```

---

## 2. Configure Environment Variables

On your **MtgDeckForge API** service in Railway, set these environment variables:

### Required

| Variable | Value | Notes |
|---|---|---|
| `LlmProvider` | `Ollama` | Switches from Claude to Ollama |
| `Ollama__BaseUrl` | `http://ollama.railway.internal:11434` | Railway internal DNS |
| `Ollama__Model` | `mistral` | Must match the model you pulled |

### Remove / Leave Unset

| Variable | Notes |
|---|---|
| `ANTHROPIC_API_KEY` | No longer needed (can remove to save costs) |
| `ClaudeApi__ApiKey` | No longer needed |

### Keep As-Is

These should already be configured from your existing Railway deployment:

| Variable | Example |
|---|---|
| `MONGODB_CONNECTION_STRING` | `mongodb://...` |
| `DATABASE_URL` | `postgresql://...` (Railway auto-injects) |
| `JWT_SECRET` | Your JWT signing key |
| `ADMIN_PASSWORD` | Admin account password |

---

## 3. Optional: `Ollama__MaxTokens`

Default is `8192`. Increase if you notice truncated deck output (the LLM cuts off before generating all 100 cards):

```
Ollama__MaxTokens=16384
```

---

## 4. Deploy

Push the `staging` branch. Railway will build and deploy automatically.

After deploy, verify:

```bash
# Health check
curl https://<your-railway-domain>/healthz

# Generate a deck (will take 30-90 seconds with Ollama)
curl -X POST https://<your-railway-domain>/api/decks/generate \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <your-jwt-token>" \
  -d '{
    "colors": ["B", "G"],
    "format": "Commander",
    "powerLevel": "Focused",
    "budgetRange": "Budget",
    "preferredCommander": "Meren of Clan Nel Toth",
    "preferredStrategy": "sacrifice and reanimation"
  }'
```

---

## 5. Switching Back to Claude

To revert to Claude at any time, change one environment variable:

```
LlmProvider=Claude
```

And ensure `ANTHROPIC_API_KEY` (or `ClaudeApi__ApiKey`) is set. Redeploy.

---

## LLM Provider Summary

| `LlmProvider` value | Backend | Use Case |
|---|---|---|
| `Claude` | Anthropic API | Production (best quality, costs money) |
| `Ollama` | Hosted Ollama (Railway) | Staging / self-hosted (free, slower) |
| `Local` | mtg-forge-local + Ollama | Local dev with RAG pipeline |

---

## Troubleshooting

**"Ollama error NotFound: model not found"**
→ Shell into the Ollama Railway service and run `ollama pull <model-name>`. Ensure `Ollama__Model` matches exactly.

**Timeout / empty response**
→ Ollama's first request after a cold start is slow (loading model into memory). Subsequent requests are faster. Consider increasing the Railway service memory if the model keeps getting evicted.

**Truncated deck (fewer than 100 cards)**
→ Increase `Ollama__MaxTokens` to `16384`. Smaller models may struggle to output a full 100-card deck in one pass.

**Response quality is poor**
→ Try a larger model (`llama3.1:8b`, `gemma2:9b`). The prompt structure is identical to what Claude uses, but smaller models may not follow JSON schemas as reliably.
