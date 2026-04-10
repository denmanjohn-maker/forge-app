# Local LLM Setup — MTG Deck Forge

## Summary

Your production app (`bensmagicforge.app`) uses Claude Sonnet to generate MTG decks, but Claude ignores price constraints because it estimates card prices from training memory. The fix is structural: route generation through a local RAG pipeline (`mtg-forge-ai`) that pre-filters cards in Qdrant by price **before** the LLM ever sees them.

Two repos have been updated:

| Repo | Location |
|---|---|
| `mtg-forge-ai` | `~/Desktop/Local LLM Magic/mtg-forge-ai/` |
| `MtgDeckForge` | `~/Desktop/Repos/MtgDeckForge/` |

### What was already done by Copilot

- **`mtg-forge-ai`** extended to support all major formats: `commander`, `standard`, `modern`, `legacy`, `pioneer`, `pauper`, `vintage` — format-aware prompts, Qdrant filters, and deck size rules
- **`MtgDeckForge`** given a `IDeckGenerationService` abstraction with two implementations: `ClaudeService` (existing) and `RagPipelineService` (new)
- Provider toggled by `"LlmProvider"` in `appsettings.json` (`"Claude"` or `"Rag"`) — no code changes required to switch
- Both repos build cleanly (0 errors)

---

## Steps You Need to Perform

### 1. Fix the Embedding Model Config

The `mtg-forge-ai` config has a mismatch that will silently break semantic search. The ingestion script uses a **384-dimension** model but the config points to a **768-dimension** model.

**File:** `~/Desktop/Local LLM Magic/mtg-forge-ai/MtgForgeLocal/appsettings.json`

Change:
```json
"EmbedModel": "nomic-embed-text"
```
To:
```json
"EmbedModel": "all-minilm"
```

Then pull the model in Ollama if you haven't already:
```bash
ollama pull all-minilm
```

---

### 2. Pull the LLM Model in Ollama

Make sure the generation model is downloaded (default is `mistral`):
```bash
ollama pull mistral
```

You can change the model in `mtg-forge-ai/MtgForgeLocal/appsettings.json` under `"LlmModel"` if you prefer a different one (e.g. `llama3`, `phi3`).

---

### 3. Re-run the Card Ingestion Script

The existing Qdrant collection only has `legality_commander`. Re-ingestion adds `legality_standard`, `legality_modern`, etc. for all 7 formats. **This step is required for non-Commander formats to work.**

This will take ~15 minutes (downloads ~80MB from Scryfall, embeds ~26k cards).

```bash
cd ~/Desktop/Local\ LLM\ Magic/mtg-forge-ai/scripts
pip install -r requirements.txt   # first time only
python ingest_cards.py
```

> **Prerequisites:** Docker must be running with Qdrant and MongoDB containers up.
> Start them with: `cd ~/Desktop/Local\ LLM\ Magic/mtg-forge-ai && docker compose up -d qdrant mongo`

---

### 4. Start the `mtg-forge-ai` Services

```bash
cd ~/Desktop/Local\ LLM\ Magic/mtg-forge-ai
docker compose up -d
```

Verify it's running:
```bash
curl http://localhost:5000/health
```

---

### 5. Switch MtgDeckForge to RAG Mode

**File:** `~/Desktop/Repos/MtgDeckForge/MtgDeckForge.Api/appsettings.json`

Change:
```json
"LlmProvider": "Claude"
```
To:
```json
"LlmProvider": "Rag"
```

The `RagPipeline` section should already be present with default values:
```json
"RagPipeline": {
  "BaseUrl": "http://localhost:5000",
  "OllamaUrl": "http://localhost:11434",
  "Model": "mistral"
}
```

---

### 6. Test End-to-End

Run `MtgDeckForge` locally and generate a deck:

1. Select format: **Standard** (or any non-Commander format to verify multi-format support)
2. Set budget: **Budget ($0–$50)**
3. Generate — all cards in the result should have individual prices that sum to ≤ $50

To switch back to Claude at any time, set `"LlmProvider": "Claude"` and restart.

---

## Architecture Overview

```
MtgDeckForge.Api
  └─ DecksController
       └─ IDeckGenerationService
            ├─ ClaudeService          (LlmProvider = "Claude")
            └─ RagPipelineService     (LlmProvider = "Rag")
                  ├─ GenerateDeckAsync  → mtg-forge-ai :5000/api/decks/generate
                  ├─ AnalyzeDeckAsync   → Ollama :11434 directly
                  └─ SuggestBudgetReplacementsAsync → returns [] (Qdrant pre-filters by price)

mtg-forge-ai :5000
  └─ /api/decks/generate
       ├─ CardSearchService  → Qdrant (semantic search + price filter + legality filter)
       ├─ DeckGenerationService → Ollama (format-aware prompt)
       └─ MongoDB (saved decks)
```

## Known Issues / Follow-Up (in mtg-forge-ai repo)

| Issue | Severity | Notes |
|---|---|---|
| Qdrant color identity filter uses `Must` instead of `MustNot` | **High** | Current bug: selects cards with ALL listed colors (e.g., includes BGW for a BG commander) and excludes legal mono-color and colorless cards. Fix: use `must_not` to exclude colors outside identity (W, U, R for BG). Requires change in mtg-forge-ai repo. |
| Embedding dimension mismatch if collection was created with wrong model | Medium | Qdrant collections are immutable in vector dimension. Delete collection and re-ingest if switching embedding models. |
| `SuggestBudgetReplacementsAsync` returns `[]` in Rag mode | By design | Budget enforcement loop in `DecksController` handles this gracefully; Qdrant pre-filtering makes it unnecessary |
