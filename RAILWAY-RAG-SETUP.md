# Railway RAG Pipeline Setup Guide

This guide covers deploying MtgDeckForge on Railway with the full RAG pipeline: **mtg-forge-local** (Qdrant vector search + Ollama LLM) instead of the Anthropic Claude API.

---

## Architecture

```
Railway project
├── mtg-api              (.NET, public ingress — this repo)
│     └──► mtg-forge-local.railway.internal:5000
├── mtg-forge-local      (.NET, internal only — separate repo)
│     ├──► qdrant.railway.internal:6333
│     └──► ollama.railway.internal:11434
├── qdrant               (Docker image: qdrant/qdrant, volume on /qdrant/storage)
├── ollama               (Docker image: ollama/ollama, volume on /root/.ollama)
├── mongodb              (deck storage)
└── postgresql           (Identity + pricing data)
```

**Why RAG instead of direct Ollama?** The direct Ollama approach uses the same "generate everything from prompts" strategy as Claude but with a less capable model — inheriting all of Claude's price hallucination and card legality issues with none of Claude's intelligence. The RAG pipeline fixes this structurally: mtg-forge-local uses Qdrant to pre-filter cards by real price, color identity, and format legality **before** the LLM ever sees them.

---

## 1. Deploy Qdrant on Railway

1. Go to your Railway project
2. Add a new service → **Docker Image** → `qdrant/qdrant`
3. Under the service settings:
   - Set **Internal Networking** hostname to `qdrant`
   - Add a **Volume** mount at `/qdrant/storage` to persist the vector index across deploys
   - **No public ingress** — only mtg-forge-local should reach it
4. Expose port **6333** internally
5. Deploy the service

---

## 2. Deploy Ollama on Railway

1. Add a new service → **Docker Image** → `ollama/ollama`
2. Under the service settings:
   - Set **Internal Networking** hostname to `ollama`
   - Add a **Volume** mount at `/root/.ollama` to persist downloaded models
   - **No public ingress**
3. Deploy the service

### Pull both models

After the Ollama service is running, open the Railway shell and run:

```bash
# Embedding model (required for Qdrant vector search)
ollama pull all-minilm

# Generation model (required for deck building + analysis)
ollama pull mistral
```

> **Important:** The embedding model (`all-minilm`, 384 dimensions) is separate from the generation model (`mistral`). Both must be pulled or ingestion/generation will fail silently.

Verify:
```bash
ollama list
# Should show both all-minilm and mistral
```

> **Model options:** `mistral` (~4.1GB) is the default. Alternatives:
> - `llama3.1:8b` (~4.7GB) — better reasoning
> - `phi3:mini` (~2.3GB) — lighter, uses less Railway RAM
> - `gemma2:9b` (~5.5GB) — good quality

---

## 3. Deploy mtg-forge-local on Railway

1. Add a new service from the **mtg-forge-local** repository
2. Under the service settings:
   - Set **Internal Networking** hostname to `mtg-forge-local`
   - **No public ingress** — only the main API should reach it
3. Set environment variables:

| Variable | Value |
|---|---|
| `Qdrant__Url` | `http://qdrant.railway.internal:6333` |
| `Ollama__Url` | `http://ollama.railway.internal:11434` |
| `Ollama__EmbedModel` | `all-minilm` |
| `Ollama__LlmModel` | `mistral` |

4. Deploy the service

### Run card ingestion (one-time)

After mtg-forge-local is running, trigger card ingestion. The recommended approach is an admin endpoint in mtg-forge-local (e.g., `POST /api/admin/ingest`) that:
- Downloads bulk card data from the Scryfall API
- Generates embeddings via Ollama's `all-minilm` model
- Stores cards + vectors in Qdrant with price, color identity, and format legality metadata
- Checks whether the collection already exists and is non-empty (safe to re-run)

Alternatively, run the existing Python ingestion script as a one-shot Railway job.

> **Qdrant dimension check:** If a collection was previously created with a different embedding model (e.g., `nomic-embed-text` at 768 dims), you must delete it first: `curl -X DELETE http://qdrant.railway.internal:6333/collections/cards`. The collection schema is immutable — a dimension mismatch will cause silent failures.

---

## 4. Configure MtgDeckForge API Environment Variables

On your **mtg-api** service in Railway, set:

### Required

| Variable | Value | Notes |
|---|---|---|
| `LlmProvider` | `Rag` | Routes to mtg-forge-local RAG pipeline |
| `RagPipeline__BaseUrl` | `http://mtg-forge-local.railway.internal:5000` | mtg-forge-local internal DNS |
| `RagPipeline__OllamaUrl` | `http://ollama.railway.internal:11434` | For deck analysis (direct Ollama calls) |
| `RagPipeline__Model` | `mistral` | Must match the model pulled in Ollama |

### Remove / Leave Unset

| Variable | Notes |
|---|---|
| `ANTHROPIC_API_KEY` | Not needed with RAG provider |
| `ClaudeApi__ApiKey` | Not needed with RAG provider |
| `Ollama__BaseUrl` | Removed — old direct Ollama config |
| `Ollama__Model` | Removed — old direct Ollama config |

### Keep As-Is

| Variable | Example |
|---|---|
| `MONGODB_CONNECTION_STRING` | `mongodb://...` |
| `DATABASE_URL` | `postgresql://...` (Railway auto-injects) |
| `JWT_SECRET` | Your JWT signing key |
| `ADMIN_PASSWORD` | Admin account password |

---

## 5. Deploy

Push the `staging` branch. Railway will build and deploy automatically.

After deploy, verify:

```bash
# Health check
curl https://<your-railway-domain>/healthz

# Generate a deck (will take 30-90 seconds via RAG pipeline)
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

## 6. Switching Back to Claude

To revert to Claude at any time, change one environment variable:

```
LlmProvider=Claude
```

And ensure `ANTHROPIC_API_KEY` (or `ClaudeApi__ApiKey`) is set. Redeploy.

---

## LLM Provider Summary

| `LlmProvider` value | Backend | Use Case |
|---|---|---|
| `Claude` | Anthropic API | Production (best quality, costs per request) |
| `Rag` | mtg-forge-local + Qdrant + Ollama | Staging / self-hosted (RAG pipeline, budget-aware, no API costs) |

---

## Cost Considerations

Ollama is the primary cost driver — it needs RAM proportional to model size:
- `mistral` (7B Q4): ~5GB RAM minimum, 8GB comfortable
- `phi3:mini` (3.8B): ~3GB RAM, cheaper option with some quality loss
- Railway charges by RAM-hours, so an always-on 8GB service has meaningful cost

Options to reduce costs:
1. Use a smaller model like `phi3:mini` (~4GB RAM)
2. Move Ollama to a serverless GPU host (RunPod, Modal) — only charges when generating

---

## Troubleshooting

**"mtg-forge-local returned 5xx"**
→ Check mtg-forge-local logs in Railway. Common issues: Qdrant not reachable, Ollama model not pulled, collection empty (ingestion not run).

**"Ollama error NotFound: model not found"**
→ Shell into the Ollama Railway service and run `ollama pull <model-name>`. Ensure the model name matches exactly in both mtg-forge-local and API env vars.

**Qdrant dimension mismatch (ingestion silently fails)**
→ Delete the existing collection (`curl -X DELETE .../collections/cards`) and re-run ingestion. Collections are immutable in vector dimension.

**Deck missing mono-color or colorless cards (Commander)**
→ This was a known bug where the Qdrant color identity filter used `Must` (requires all listed colors) instead of `MustNot` (excludes colors outside identity). Ensure mtg-forge-local uses `must_not` filters for color identity. See the Known Issues section in LOCAL-LLM-SETUP.md.

**Timeout / slow first request**
→ Ollama's first request after a cold start is slow (loading model into memory). Subsequent requests are faster.

**Truncated deck output**
→ The generation model may not produce all 100 cards. mtg-forge-local should handle padding; if not, check the model's token limit configuration.
