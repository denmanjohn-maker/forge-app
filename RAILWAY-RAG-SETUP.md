# Railway RAG Pipeline Setup Guide

This guide covers deploying MtgDeckForge on Railway with the full RAG pipeline: **mtg-forge-ai** (Qdrant vector search + Ollama LLM) instead of the Anthropic Claude API.

---

## Architecture

```
Railway project
├── mtg-api              (.NET, public ingress — this repo)
│     └──► mtg-forge-ai.railway.internal:5000
├── mtg-forge-ai      (.NET, internal only — separate repo)
│     ├──► qdrant.railway.internal:6333
│     └──► ollama.railway.internal:11434
├── qdrant               (Docker image: qdrant/qdrant, volume on /qdrant/storage)
├── ollama               (Docker image: ollama/ollama, volume on /root/.ollama)
├── mongodb              (deck storage)
└── postgresql           (Identity + pricing data)
```

**Why RAG instead of direct Ollama?** The direct Ollama approach uses the same "generate everything from prompts" strategy as Claude but with a less capable model — inheriting all of Claude's price hallucination and card legality issues with none of Claude's intelligence. The RAG pipeline fixes this structurally: mtg-forge-ai uses Qdrant to pre-filter cards by real price, color identity, and format legality **before** the LLM ever sees them.

---

## 1. Deploy Qdrant on Railway

1. Go to your Railway project
2. Add a new service → **Docker Image** → `qdrant/qdrant`
3. Under the service settings:
   - Set **Internal Networking** hostname to `qdrant`
   - Add a **Volume** mount at `/qdrant/storage` to persist the vector index across deploys
   - **No public ingress** — only mtg-forge-ai should reach it
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

## 3. Deploy mtg-forge-ai on Railway

The `mtg-forge-ai` service lives in its own repo: **[mtg-forge-card-ai](https://github.com/denmanjohn-maker/mtg-forge-card-ai)**.

1. In your Railway project, add a new service → **GitHub repo** → select `mtg-forge-card-ai`
   - Railway will auto-detect the Dockerfile at `MtgForgeLocal/Dockerfile`
2. Under the service settings:
   - Set **Internal Networking** hostname to `mtg-forge-ai`
   - **No public ingress** — only the main API should reach it
   - The container listens on **port 8080** (set by the Dockerfile)
3. Set environment variables:

| Variable | Value |
|---|---|
| `Qdrant__Host` | `qdrant.railway.internal` |
| `Qdrant__Port` | `6334` |
| `Ollama__BaseUrl` | `http://ollama.railway.internal:11434` |
| `Ollama__Model` | `mistral` |
| `Ollama__EmbedModel` | `all-minilm` |
| `MongoDB__ConnectionString` | `<your Railway MongoDB internal URL>` |
| `MongoDB__DatabaseName` | `mtgforge` |

4. Deploy the service

### Run card ingestion (one-time)

Card ingestion populates the Qdrant vector index with MTG card data (prices, color identity, format legality). It must be run once before the RAG pipeline can generate decks.

#### Where to run these commands

`mtg-forge-ai` has **no public ingress** — it is only reachable on Railway's internal network. All `curl` commands below must be run from **Railway's built-in shell**, not your local terminal.

To open a Railway shell:
1. Go to your Railway project dashboard
2. Click the **mtg-forge-ai** service
3. Click the **Deploy** tab → **Shell** (or press the terminal icon in the top-right)

This drops you into a shell inside the running container, where you can reach both `localhost:5000` (mtg-forge-ai itself) and the internal hostnames like `qdrant.railway.internal`.

#### Step 1 — (Optional) Clear a stale Qdrant collection

Skip this step on a fresh setup. Only run it if you previously ingested with a different embedding model (e.g., switching from `nomic-embed-text` at 768 dims to `all-minilm` at 384 dims). The collection schema is immutable — a dimension mismatch will cause silent failures.

```bash
# Run from the Railway shell of any service (mtg-forge-ai, mtg-api, etc.)
curl -X DELETE http://qdrant.railway.internal:6333/collections/cards
# Expected response: {"result":true,"status":"ok","time":...}
```

#### Step 2 — Trigger ingestion

Open the Railway shell inside the **mtg-forge-ai** service (Dashboard → mtg-forge-ai → Deploy tab → Shell).

Full ingest (all cards from Scryfall — takes 20-60+ minutes):

```bash
curl -X POST http://localhost:8080/api/admin/ingest \
  -H "Content-Type: application/json" \
  -d '{}'
```

For a quick smoke test with a limited card count (~2-3 minutes):

```bash
curl -X POST http://localhost:8080/api/admin/ingest \
  -H "Content-Type: application/json" \
  -d '{"limit": 1000}'
```

Optional body fields:

| Field | Type | Description |
|---|---|---|
| `limit` | int? | Cap the number of cards ingested (omit for all ~30k cards) |
| `mongoOnly` | bool | Only write to MongoDB, skip Qdrant embedding |
| `qdrantOnly` | bool | Only (re-)embed into Qdrant from existing MongoDB data |

> **This will take a while.** Scryfall bulk data is ~250MB and embedding each card via Ollama is slow. Watch the mtg-forge-ai service logs in Railway for progress.

#### Step 3 — Verify the collection was populated

Still in the Railway shell, check that Qdrant has cards:

```bash
curl http://qdrant.railway.internal:6333/collections/cards
```

Look for `"vectors_count"` in the response — a successful ingest will show tens of thousands of vectors:

```json
{
  "result": {
    "status": "green",
    "vectors_count": 28000,
    ...
  }
}
```

If `vectors_count` is `0` or the collection doesn't exist, check the mtg-forge-ai logs for errors (Qdrant unreachable, Ollama model not found, etc.).

---

## 4. Configure MtgDeckForge API Environment Variables

On your **mtg-api** service in Railway, set:

### Required

| Variable | Value | Notes |
|---|---|---|
| `LlmProvider` | `Rag` | Routes to mtg-forge-ai RAG pipeline |
| `RagPipeline__BaseUrl` | `http://mtg-forge-ai.railway.internal:8080` | mtg-forge-ai internal DNS |
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
| `MongoDb__ConnectionString` | `mongodb://...` |
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
| `Rag` | mtg-forge-ai + Qdrant + Ollama | Staging / self-hosted (RAG pipeline, budget-aware, no API costs) |

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

**"mtg-forge-ai returned 5xx"**
→ Check mtg-forge-ai logs in Railway. Common issues: Qdrant not reachable, Ollama model not pulled, collection empty (ingestion not run).

**"Ollama error NotFound: model not found"**
→ Shell into the Ollama Railway service and run `ollama pull <model-name>`. Ensure the model name matches exactly in both mtg-forge-ai and API env vars.

**Qdrant dimension mismatch (ingestion silently fails)**
→ Delete the existing collection (`curl -X DELETE .../collections/cards`) and re-run ingestion. Collections are immutable in vector dimension.

**Deck missing mono-color or colorless cards (Commander)**
→ This was a known bug where the Qdrant color identity filter used `Must` (requires all listed colors) instead of `MustNot` (excludes colors outside identity). Ensure mtg-forge-ai uses `must_not` filters for color identity. See the Known Issues section in LOCAL-LLM-SETUP.md.

**Timeout / slow first request**
→ Ollama's first request after a cold start is slow (loading model into memory). Subsequent requests are faster.

**Truncated deck output**
→ The generation model may not produce all 100 cards. mtg-forge-ai should handle padding; if not, check the model's token limit configuration.
